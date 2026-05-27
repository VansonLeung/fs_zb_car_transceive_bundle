#include "fpv4win_bridge.h"

#include <winsock2.h>
#include <ws2tcpip.h>

#include <libusb.h>

#include <algorithm>
#include <array>
#include <atomic>
#include <cctype>
#include <cstdint>
#include <cstring>
#include <exception>
#include <iomanip>
#include <memory>
#include <mutex>
#include <sstream>
#include <string>
#include <thread>
#include <vector>

#include "Rtp.h"
#include "RxFrame.h"
#include "SelectedChannel.h"
#include "WFBProcessor.h"
#include "WiFiDriver.h"
#include "logger.h"

namespace {

std::mutex g_errorMutex;
std::string g_lastError = "Bridge not initialized.";

void SetBridgeError(const std::string &message) {
    std::lock_guard<std::mutex> lock(g_errorMutex);
    g_lastError = message;
}

const char *GetBridgeErrorCStr() {
    thread_local std::string snapshot;
    {
        std::lock_guard<std::mutex> lock(g_errorMutex);
        snapshot = g_lastError;
    }
    return snapshot.c_str();
}

bool ParseVidPid(const std::string &text, uint16_t &vid, uint16_t &pid) {
    const auto delimiter = text.find(':');
    if (delimiter == std::string::npos) {
        return false;
    }

    const auto vidText = text.substr(0, delimiter);
    const auto pidText = text.substr(delimiter + 1);

    if (vidText.size() != 4 || pidText.size() != 4) {
        return false;
    }

    if (!std::all_of(vidText.begin(), vidText.end(), [](unsigned char c) { return std::isxdigit(c) != 0; })) {
        return false;
    }

    if (!std::all_of(pidText.begin(), pidText.end(), [](unsigned char c) { return std::isxdigit(c) != 0; })) {
        return false;
    }

    vid = static_cast<uint16_t>(std::stoul(vidText, nullptr, 16));
    pid = static_cast<uint16_t>(std::stoul(pidText, nullptr, 16));
    return true;
}

ChannelWidth_t ToChannelWidth(int widthIndex) {
    const int clamped = std::clamp(widthIndex, 0, static_cast<int>(CHANNEL_WIDTH_MAX) - 1);
    return static_cast<ChannelWidth_t>(clamped);
}

std::string JsonEscape(const std::string &value) {
    std::ostringstream escaped;

    for (const unsigned char ch : value) {
        switch (ch) {
        case '\\':
            escaped << "\\\\";
            break;
        case '"':
            escaped << "\\\"";
            break;
        case '\b':
            escaped << "\\b";
            break;
        case '\f':
            escaped << "\\f";
            break;
        case '\n':
            escaped << "\\n";
            break;
        case '\r':
            escaped << "\\r";
            break;
        case '\t':
            escaped << "\\t";
            break;
        default:
            if (ch < 0x20) {
                escaped << "\\u" << std::hex << std::setw(4) << std::setfill('0') << static_cast<int>(ch)
                        << std::dec << std::setfill(' ');
            } else {
                escaped << static_cast<char>(ch);
            }
            break;
        }
    }

    return escaped.str();
}

std::string NormalizeCodec(std::string value) {
    std::transform(value.begin(), value.end(), value.begin(), [](unsigned char ch) {
        return static_cast<char>(std::toupper(ch));
    });
    return value;
}

bool IsH264Payload(const uint8_t *payloadData) {
    if (payloadData == nullptr) {
        return false;
    }

    const auto nalType = payloadData[0] & 0x1F;
    return nalType == 24 || nalType == 28;
}

class BridgeReceiver {
public:
    BridgeReceiver() {
        WSADATA wsaData {};
        if (WSAStartup(MAKEWORD(2, 2), &wsaData) != 0) {
            SetBridgeError("WSAStartup failed.");
            return;
        }

        wsaReady_ = true;
        sendSocket_ = socket(AF_INET, SOCK_DGRAM, 0);
        if (sendSocket_ == INVALID_SOCKET) {
            SetBridgeError("Failed to create UDP socket.");
            return;
        }

        constexpr uint32_t linkId = 7669206;
        constexpr uint8_t videoRadioPort = 0;
        videoChannelId_ = (linkId << 8) + videoRadioPort;
        const auto channelIdBe = htobe32(videoChannelId_);
        std::memcpy(videoChannelIdBytes_.data(), &channelIdBe, videoChannelIdBytes_.size());
    }

    ~BridgeReceiver() {
        Stop();

        if (sendSocket_ != INVALID_SOCKET) {
            closesocket(sendSocket_);
            sendSocket_ = INVALID_SOCKET;
        }

        if (wsaReady_) {
            WSACleanup();
            wsaReady_ = false;
        }
    }

    bool Start(
        const std::string &vidPid,
        int channel,
        int channelWidthIndex,
        const std::string &keyPath,
        const std::string &codec,
        int playerPort) {
        if (sendSocket_ == INVALID_SOCKET) {
            SetBridgeError("Bridge socket is not available.");
            return false;
        }

        std::lock_guard<std::mutex> lock(stateMutex_);
        if (running_.load()) {
            SetBridgeError("Bridge is already running.");
            return false;
        }

        uint16_t vid = 0;
        uint16_t pid = 0;
        if (!ParseVidPid(vidPid, vid, pid)) {
            SetBridgeError("Invalid VID:PID format. Expected XXXX:XXXX.");
            return false;
        }

        if (channel < 1 || channel > 255) {
            SetBridgeError("Invalid channel index.");
            return false;
        }

        if (keyPath.empty()) {
            SetBridgeError("WFB key path is empty.");
            return false;
        }

        playerPort_ = playerPort > 0 ? playerPort : 52356;
        channel_ = static_cast<uint8_t>(channel);
        channelWidth_ = ToChannelWidth(channelWidthIndex);
        requestedCodec_ = NormalizeCodec(codec.empty() ? "AUTO" : codec);
        ResetRuntimeStatus();

        try {
            if (!aggregator_ || !std::equal_to {}(aggregatorKeyPath_, keyPath)) {
                aggregator_ = std::make_unique<Aggregator>(
                    keyPath,
                    0,
                    videoChannelId_,
                    [this](uint8_t *payload, uint16_t packetSize) { HandleRtp(payload, packetSize); });
                aggregatorKeyPath_ = keyPath;
            }
        } catch (const std::exception &ex) {
            SetBridgeError(ex.what());
            return false;
        }

        int rc = libusb_init(&ctx_);
        if (rc < 0) {
            SetBridgeError("libusb_init failed.");
            return false;
        }

        devHandle_ = libusb_open_device_with_vid_pid(ctx_, vid, pid);
        if (devHandle_ == nullptr) {
            SetBridgeError("Cannot open selected USB adapter via libusb.");
            libusb_exit(ctx_);
            ctx_ = nullptr;
            return false;
        }

        if (libusb_kernel_driver_active(devHandle_, 0) == 1) {
            libusb_detach_kernel_driver(devHandle_, 0);
        }

        rc = libusb_claim_interface(devHandle_, 0);
        if (rc < 0) {
            SetBridgeError("libusb_claim_interface failed.");
            libusb_close(devHandle_);
            devHandle_ = nullptr;
            libusb_exit(ctx_);
            ctx_ = nullptr;
            return false;
        }

        interfaceClaimed_ = true;
        running_.store(true);
        usbThread_ = std::thread([this]() { RunUsbLoop(); });
        SetBridgeError("Bridge running.");
        return true;
    }

    bool Stop() {
        std::thread loopThread;
        {
            std::lock_guard<std::mutex> lock(stateMutex_);
            if (rtlDevice_) {
                rtlDevice_->should_stop = true;
            }
            if (usbThread_.joinable()) {
                loopThread = std::move(usbThread_);
            }
        }

        if (loopThread.joinable()) {
            loopThread.join();
        }

        std::lock_guard<std::mutex> lock(stateMutex_);
        running_.store(false);
        rtlDevice_.reset();
        ResetRuntimeStatus();

        if (interfaceClaimed_ && devHandle_ != nullptr) {
            libusb_release_interface(devHandle_, 0);
            interfaceClaimed_ = false;
        }

        if (devHandle_ != nullptr) {
            libusb_close(devHandle_);
            devHandle_ = nullptr;
        }

        if (ctx_ != nullptr) {
            libusb_exit(ctx_);
            ctx_ = nullptr;
        }

        return true;
    }

    std::string GetStatusJson() const {
        std::string codec;
        int payloadType = -1;
        uint32_t ssrc = 0;
        bool streamReady = false;
        uint32_t decryptErrorCount = 0;
        uint32_t decodedPacketCount = 0;
        uint32_t badPacketCount = 0;
        bool sessionReady = false;

        {
            std::lock_guard<std::mutex> lock(streamInfoMutex_);
            codec = resolvedCodec_;
            payloadType = payloadType_;
            ssrc = ssrc_;
            streamReady = streamReady_;
        }

        {
            std::lock_guard<std::mutex> lock(frameMutex_);
            if (aggregator_) {
                decryptErrorCount = aggregator_->GetDecryptErrorCount();
                decodedPacketCount = aggregator_->GetDecodedPacketCount();
                badPacketCount = aggregator_->GetBadPacketCount();
                sessionReady = aggregator_->HasActiveSession();
            }
        }

        std::ostringstream json;
        json << "{";
        json << "\"running\":" << (running_.load() ? "true" : "false");
        json << ",\"playerPort\":" << playerPort_;
        json << ",\"wifiFrameCount\":" << wifiFrameCount_.load();
        json << ",\"wfbFrameCount\":" << wfbFrameCount_.load();
        json << ",\"matchedFrameCount\":" << matchedFrameCount_.load();
        json << ",\"matchedDataPacketCount\":" << matchedDataPacketCount_.load();
        json << ",\"matchedSessionKeyPacketCount\":" << matchedSessionKeyPacketCount_.load();
        json << ",\"matchedUnknownPacketCount\":" << matchedUnknownPacketCount_.load();
        json << ",\"rtpPktCount\":" << rtpPacketCount_.load();
        json << ",\"streamReady\":" << (streamReady ? "true" : "false");
        json << ",\"sessionReady\":" << (sessionReady ? "true" : "false");
        json << ",\"decryptErrorCount\":" << decryptErrorCount;
        json << ",\"decodedPacketCount\":" << decodedPacketCount;
        json << ",\"badPacketCount\":" << badPacketCount;
        json << ",\"payloadType\":" << payloadType;
        json << ",\"ssrc\":" << ssrc;
        json << ",\"codec\":\"" << JsonEscape(codec) << "\"";
        json << "}";
        return json.str();
    }

private:
    void ResetRuntimeStatus() {
        wifiFrameCount_.store(0);
        wfbFrameCount_.store(0);
        matchedFrameCount_.store(0);
        matchedDataPacketCount_.store(0);
        matchedSessionKeyPacketCount_.store(0);
        matchedUnknownPacketCount_.store(0);
        rtpPacketCount_.store(0);

        std::lock_guard<std::mutex> lock(streamInfoMutex_);
        payloadType_ = -1;
        ssrc_ = 0;
        streamReady_ = false;
        resolvedCodec_.clear();
    }

    void RunUsbLoop() {
        auto logger = std::make_shared<Logger>([](const std::string &, const std::string &) {});

        try {
            WiFiDriver wifiDriver { logger };
            {
                std::lock_guard<std::mutex> lock(stateMutex_);
                rtlDevice_ = wifiDriver.CreateRtlDevice(devHandle_);
            }

            SelectedChannel selectedChannel {};
            selectedChannel.Channel = channel_;
            selectedChannel.ChannelOffset = 0;
            selectedChannel.ChannelWidth = channelWidth_;

            rtlDevice_->Init(
                [this](const Packet &packet) { Handle80211Frame(packet); },
                selectedChannel);
        } catch (const std::exception &ex) {
            SetBridgeError(ex.what());
        } catch (...) {
            SetBridgeError("Native bridge receiver failed with an unknown error.");
        }

        std::lock_guard<std::mutex> lock(stateMutex_);
        running_.store(false);
        rtlDevice_.reset();
    }

    void Handle80211Frame(const Packet &packet) {
        if (!running_.load()) {
            return;
        }

        wifiFrameCount_.fetch_add(1);

        if (packet.Data.size() <= sizeof(ieee80211_header) + 4) {
            return;
        }

        RxFrame frame(packet.Data);
        if (!frame.IsValidWfbFrame()) {
            return;
        }

        wfbFrameCount_.fetch_add(1);

        if (!frame.MatchesChannelID(videoChannelIdBytes_.data())) {
            return;
        }

        matchedFrameCount_.fetch_add(1);

        const auto *forwarderPayload = packet.Data.data() + sizeof(ieee80211_header);
        const auto forwarderSize = packet.Data.size() - sizeof(ieee80211_header) - 4;
        if (forwarderSize > 0) {
            switch (forwarderPayload[0]) {
            case WFB_PACKET_DATA:
                matchedDataPacketCount_.fetch_add(1);
                break;
            case WFB_PACKET_KEY:
                matchedSessionKeyPacketCount_.fetch_add(1);
                break;
            default:
                matchedUnknownPacketCount_.fetch_add(1);
                break;
            }
        }

        std::lock_guard<std::mutex> lock(frameMutex_);
        if (!aggregator_) {
            return;
        }

        aggregator_->process_packet(
            forwarderPayload,
            forwarderSize,
            0,
            antenna_.data(),
            rssi_.data());
    }

    void HandleRtp(uint8_t *payload, uint16_t packetSize) {
        if (sendSocket_ == INVALID_SOCKET || payload == nullptr || packetSize == 0) {
            return;
        }

        rtpPacketCount_.fetch_add(1);

        if (packetSize >= 12) {
            auto *header = reinterpret_cast<RtpHeader *>(payload);
            std::lock_guard<std::mutex> lock(streamInfoMutex_);
            if (!streamReady_) {
                payloadType_ = static_cast<int>(header->pt);
                ssrc_ = ntohl(header->ssrc);
                resolvedCodec_ = requestedCodec_ == "AUTO"
                    ? (IsH264Payload(header->getPayloadData()) ? "H264" : "H265")
                    : requestedCodec_;
                streamReady_ = true;
            }
        }

        sockaddr_in serverAddr {};
        serverAddr.sin_family = AF_INET;
        serverAddr.sin_port = htons(static_cast<u_short>(playerPort_));
        inet_pton(AF_INET, "127.0.0.1", &serverAddr.sin_addr);

        const int rc = sendto(
            sendSocket_,
            reinterpret_cast<const char *>(payload),
            packetSize,
            0,
            reinterpret_cast<const sockaddr *>(&serverAddr),
            sizeof(serverAddr));

        if (rc == SOCKET_ERROR) {
            SetBridgeError("Failed to forward RTP packet to localhost UDP port.");
        }
    }

private:
    std::mutex stateMutex_;
    mutable std::mutex frameMutex_;
    mutable std::mutex streamInfoMutex_;

    std::atomic<bool> running_ { false };
    std::thread usbThread_;

    libusb_context *ctx_ = nullptr;
    libusb_device_handle *devHandle_ = nullptr;
    bool interfaceClaimed_ = false;

    std::unique_ptr<Rtl8812aDevice> rtlDevice_;
    std::unique_ptr<Aggregator> aggregator_;
    std::string aggregatorKeyPath_;

    SOCKET sendSocket_ = INVALID_SOCKET;
    bool wsaReady_ = false;

    uint8_t channel_ = 149;
    ChannelWidth_t channelWidth_ = CHANNEL_WIDTH_40;
    int playerPort_ = 52356;
    std::string requestedCodec_ = "AUTO";
    std::string resolvedCodec_;
    int payloadType_ = -1;
    uint32_t ssrc_ = 0;
    bool streamReady_ = false;

    std::atomic<uint64_t> wifiFrameCount_ { 0 };
    std::atomic<uint64_t> wfbFrameCount_ { 0 };
    std::atomic<uint64_t> matchedFrameCount_ { 0 };
    std::atomic<uint64_t> matchedDataPacketCount_ { 0 };
    std::atomic<uint64_t> matchedSessionKeyPacketCount_ { 0 };
    std::atomic<uint64_t> matchedUnknownPacketCount_ { 0 };
    std::atomic<uint64_t> rtpPacketCount_ { 0 };

    uint32_t videoChannelId_ = 0;
    std::array<uint8_t, 4> videoChannelIdBytes_ { 0, 0, 0, 0 };
    std::array<uint8_t, 4> antenna_ { 1, 1, 1, 1 };
    std::array<int8_t, 4> rssi_ { 1, 1, 1, 1 };
};

std::mutex g_bridgeMutex;
std::unique_ptr<BridgeReceiver> g_receiver;

BridgeReceiver *EnsureReceiver() {
    if (!g_receiver) {
        g_receiver = std::make_unique<BridgeReceiver>();
    }
    return g_receiver.get();
}

} // namespace

extern "C" FPV4WIN_BRIDGE_API int fpv4win_bridge_probe(void) {
    if (sodium_init() < 0) {
        SetBridgeError("sodium_init failed.");
        return 0;
    }

    libusb_context *ctx = nullptr;
    if (libusb_init(&ctx) < 0) {
        SetBridgeError("libusb_init probe failed.");
        return 0;
    }
    libusb_exit(ctx);

    std::lock_guard<std::mutex> lock(g_bridgeMutex);
    EnsureReceiver();
    SetBridgeError("Bridge probe succeeded.");
    return 1;
}

extern "C" FPV4WIN_BRIDGE_API int fpv4win_bridge_start(
    const char *vidPid,
    int channel,
    int channelWidthIndex,
    const char *keyPath,
    const char *codec,
    int playerPort) {
    std::lock_guard<std::mutex> lock(g_bridgeMutex);
    auto *receiver = EnsureReceiver();
    if (receiver == nullptr) {
        SetBridgeError("Bridge receiver allocation failed.");
        return 0;
    }

    const std::string vidPidText = vidPid == nullptr ? "" : vidPid;
    const std::string keyPathText = keyPath == nullptr ? "" : keyPath;
    const std::string codecText = codec == nullptr ? "AUTO" : codec;

    return receiver->Start(vidPidText, channel, channelWidthIndex, keyPathText, codecText, playerPort) ? 1 : 0;
}

extern "C" FPV4WIN_BRIDGE_API int fpv4win_bridge_stop(void) {
    std::lock_guard<std::mutex> lock(g_bridgeMutex);
    if (!g_receiver) {
        SetBridgeError("Bridge stopped.");
        return 1;
    }

    return g_receiver->Stop() ? 1 : 0;
}

extern "C" FPV4WIN_BRIDGE_API const char *fpv4win_bridge_get_last_error(void) {
    return GetBridgeErrorCStr();
}

extern "C" FPV4WIN_BRIDGE_API const char *fpv4win_bridge_get_status_json(void) {
    thread_local std::string snapshot;

    std::lock_guard<std::mutex> lock(g_bridgeMutex);
    auto *receiver = EnsureReceiver();
    snapshot = receiver != nullptr ? receiver->GetStatusJson() : "{}";
    return snapshot.c_str();
}
