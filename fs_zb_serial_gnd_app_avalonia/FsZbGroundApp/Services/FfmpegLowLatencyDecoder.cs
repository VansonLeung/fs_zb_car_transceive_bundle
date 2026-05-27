using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using FFmpeg.AutoGen;

namespace FsZbGroundApp.Services;

public sealed unsafe class FfmpegLowLatencyDecoder : IDisposable
{
    private const long ReadTimeoutMs = 100;

    private readonly InterruptState _interruptState = new();
    private readonly AVIOInterruptCB_callback _interruptCallback;
    private GCHandle _interruptStateHandle;
    private AVFormatContext* _formatContext;
    private AVCodecContext* _codecContext;
    private AVPacket* _packet;
    private AVFrame* _decodedFrame;
    private AVFrame* _bgraFrame;
    private SwsContext* _swsContext;
    private int _videoStreamIndex = -1;
    private int _currentWidth;
    private int _currentHeight;
    private AVPixelFormat _currentSourceFormat = AVPixelFormat.AV_PIX_FMT_NONE;
    private bool _disposed;

    public FfmpegLowLatencyDecoder()
    {
        _interruptCallback = InterruptCallback;
    }

    public string? LastError { get; private set; }

    public bool TryOpen(string inputPath)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(inputPath))
        {
            LastError = "FFmpeg decoder input path is empty.";
            return false;
        }

        ffmpeg.avformat_network_init();

        if (!_interruptStateHandle.IsAllocated)
        {
            _interruptStateHandle = GCHandle.Alloc(_interruptState);
        }

        _interruptState.CancelRequested = false;
        _formatContext = ffmpeg.avformat_alloc_context();
        if (_formatContext is null)
        {
            LastError = "FFmpeg could not allocate an input format context.";
            return false;
        }

        _formatContext->interrupt_callback.callback = _interruptCallback;
        _formatContext->interrupt_callback.opaque = (void*)GCHandle.ToIntPtr(_interruptStateHandle);

        AVDictionary* options = null;
        try
        {
            SetOption(&options, "protocol_whitelist", "file,udp,rtp,tcp,http,https");
            SetOption(&options, "fflags", "nobuffer");
            SetOption(&options, "flags", "low_delay");
            SetOption(&options, "avioflags", "direct");
            SetOption(&options, "buffer_size", "425984");
            SetOption(&options, "max_delay", "0");
            SetOption(&options, "reorder_queue_size", "0");
            SetOption(&options, "probesize", "2048");
            SetOption(&options, "analyzeduration", "0");
            SetOption(&options, "preset", "ultrafast");
            SetOption(&options, "tune", "zerolatency");

            AVInputFormat* inputFormat = ffmpeg.av_find_input_format("sdp");
            var formatContext = _formatContext;
            var openResult = ffmpeg.avformat_open_input(&formatContext, inputPath, inputFormat, &options);
            _formatContext = formatContext;
            if (openResult < 0)
            {
                LastError = $"FFmpeg could not open '{inputPath}': {GetErrorText(openResult)}";
                return false;
            }
        }
        finally
        {
            ffmpeg.av_dict_free(&options);
        }

        var streamInfoResult = ffmpeg.avformat_find_stream_info(_formatContext, null);
        if (streamInfoResult < 0)
        {
            LastError = $"FFmpeg could not read SDP stream info: {GetErrorText(streamInfoResult)}";
            return false;
        }

        _videoStreamIndex = ffmpeg.av_find_best_stream(_formatContext, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, null, 0);
        if (_videoStreamIndex < 0)
        {
            LastError = $"FFmpeg could not locate a video stream: {GetErrorText(_videoStreamIndex)}";
            return false;
        }

        var codecParameters = _formatContext->streams[_videoStreamIndex]->codecpar;
        var codec = ffmpeg.avcodec_find_decoder(codecParameters->codec_id);
        if (codec is null)
        {
            LastError = $"FFmpeg could not find a decoder for codec id {(int)codecParameters->codec_id}.";
            return false;
        }

        _codecContext = ffmpeg.avcodec_alloc_context3(codec);
        if (_codecContext is null)
        {
            LastError = "FFmpeg could not allocate a decoder context.";
            return false;
        }

        var parametersResult = ffmpeg.avcodec_parameters_to_context(_codecContext, codecParameters);
        if (parametersResult < 0)
        {
            LastError = $"FFmpeg could not apply decoder parameters: {GetErrorText(parametersResult)}";
            return false;
        }

        _codecContext->thread_count = 1;
        _codecContext->flags2 |= ffmpeg.AV_CODEC_FLAG2_FAST;
        _codecContext->flags |= ffmpeg.AV_CODEC_FLAG_LOW_DELAY;

        var openCodecResult = ffmpeg.avcodec_open2(_codecContext, codec, null);
        if (openCodecResult < 0)
        {
            LastError = $"FFmpeg could not open the video decoder: {GetErrorText(openCodecResult)}";
            return false;
        }

        _packet = ffmpeg.av_packet_alloc();
        _decodedFrame = ffmpeg.av_frame_alloc();
        _bgraFrame = ffmpeg.av_frame_alloc();

        if (_packet is null || _decodedFrame is null || _bgraFrame is null)
        {
            LastError = "FFmpeg could not allocate decode buffers.";
            return false;
        }

        LastError = null;
        return true;
    }

    public void CancelPendingRead()
    {
        _interruptState.CancelRequested = true;
    }

    public bool TryReadFrame(out DecodedVideoFrame? frame)
    {
        ThrowIfDisposed();
        frame = null;

        if (_formatContext is null || _codecContext is null || _packet is null || _decodedFrame is null || _bgraFrame is null)
        {
            LastError = "FFmpeg decoder was not initialized.";
            return false;
        }

        while (!_interruptState.CancelRequested)
        {
            var pendingFrameResult = TryReceiveDecodedFrame(out frame);
            if (pendingFrameResult > 0)
            {
                return true;
            }

            if (pendingFrameResult == ffmpeg.AVERROR_EOF)
            {
                LastError = "FFmpeg video decoder reached end of stream.";
                return false;
            }

            if (pendingFrameResult < 0 && pendingFrameResult != ffmpeg.AVERROR(ffmpeg.EAGAIN))
            {
                LastError = $"FFmpeg could not decode a video frame: {GetErrorText(pendingFrameResult)}";
                return false;
            }

            _interruptState.ReadTimedOut = false;
            _interruptState.ReadDeadlineTickCount = Environment.TickCount64 + ReadTimeoutMs;
            int readResult;
            try
            {
                readResult = ffmpeg.av_read_frame(_formatContext, _packet);
            }
            finally
            {
                _interruptState.ReadDeadlineTickCount = long.MaxValue;
            }
            if (readResult == ffmpeg.AVERROR_EOF)
            {
                var flushResult = ffmpeg.avcodec_send_packet(_codecContext, null);
                if (flushResult < 0 && flushResult != ffmpeg.AVERROR_EOF && flushResult != ffmpeg.AVERROR(ffmpeg.EAGAIN))
                {
                    LastError = $"FFmpeg could not flush the video decoder: {GetErrorText(flushResult)}";
                    return false;
                }

                var finalFrameResult = TryReceiveDecodedFrame(out frame);
                if (finalFrameResult > 0)
                {
                    return true;
                }

                LastError = finalFrameResult == ffmpeg.AVERROR_EOF
                    ? "FFmpeg reached end of stream."
                    : $"FFmpeg reached end of stream: {GetErrorText(finalFrameResult)}";
                return false;
            }

            if (readResult == ffmpeg.AVERROR(ffmpeg.EAGAIN))
            {
                continue;
            }

            if (readResult < 0)
            {
                if (_interruptState.ReadTimedOut)
                {
                    LastError = "FFmpeg read timed out while waiting for RTP packets.";
                    continue;
                }

                LastError = $"FFmpeg could not read from the RTP stream: {GetErrorText(readResult)}";
                return false;
            }

            if (_packet->stream_index != _videoStreamIndex)
            {
                ffmpeg.av_packet_unref(_packet);
                continue;
            }

            var sendResult = ffmpeg.avcodec_send_packet(_codecContext, _packet);
            ffmpeg.av_packet_unref(_packet);

            if (sendResult < 0 && sendResult != ffmpeg.AVERROR(ffmpeg.EAGAIN))
            {
                LastError = $"FFmpeg could not submit a video packet: {GetErrorText(sendResult)}";
                return false;
            }
        }

        LastError = "FFmpeg decode loop was canceled.";
        return false;
    }

    private int TryReceiveDecodedFrame(out DecodedVideoFrame? frame)
    {
        frame = null;

        var receiveResult = ffmpeg.avcodec_receive_frame(_codecContext, _decodedFrame);
        if (receiveResult < 0)
        {
            return receiveResult;
        }

        if (!EnsureBgraOutputFrame((AVPixelFormat)_decodedFrame->format, _decodedFrame->width, _decodedFrame->height))
        {
            ffmpeg.av_frame_unref(_decodedFrame);
            return ffmpeg.AVERROR(ffmpeg.EINVAL);
        }

        var writableResult = ffmpeg.av_frame_make_writable(_bgraFrame);
        if (writableResult < 0)
        {
            ffmpeg.av_frame_unref(_decodedFrame);
            LastError = $"FFmpeg could not prepare the BGRA output frame: {GetErrorText(writableResult)}";
            return writableResult;
        }

        var scaleResult = ffmpeg.sws_scale_frame(_swsContext, _bgraFrame, _decodedFrame);
        ffmpeg.av_frame_unref(_decodedFrame);

        if (scaleResult < 0)
        {
            LastError = $"FFmpeg could not convert the decoded frame to BGRA: {GetErrorText(scaleResult)}";
            return scaleResult;
        }

        var stride = _bgraFrame->linesize[0];
        var bufferLength = stride * _bgraFrame->height;
        var managedBuffer = ArrayPool<byte>.Shared.Rent(bufferLength);
        Marshal.Copy((IntPtr)_bgraFrame->data[0], managedBuffer, 0, bufferLength);

        frame = DecodedVideoFrame.Rent(managedBuffer, bufferLength, _bgraFrame->width, _bgraFrame->height, stride);
        LastError = null;
        return 1;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _interruptState.CancelRequested = true;

        if (_swsContext is not null)
        {
            ffmpeg.sws_freeContext(_swsContext);
            _swsContext = null;
        }

        if (_bgraFrame is not null)
        {
            var bgraFrame = _bgraFrame;
            ffmpeg.av_frame_free(&bgraFrame);
            _bgraFrame = bgraFrame;
        }

        if (_decodedFrame is not null)
        {
            var decodedFrame = _decodedFrame;
            ffmpeg.av_frame_free(&decodedFrame);
            _decodedFrame = decodedFrame;
        }

        if (_packet is not null)
        {
            var packet = _packet;
            ffmpeg.av_packet_free(&packet);
            _packet = packet;
        }

        if (_codecContext is not null)
        {
            var codecContext = _codecContext;
            ffmpeg.avcodec_free_context(&codecContext);
            _codecContext = codecContext;
        }

        if (_formatContext is not null)
        {
            var formatContext = _formatContext;
            ffmpeg.avformat_close_input(&formatContext);
            _formatContext = formatContext;
        }

        if (_interruptStateHandle.IsAllocated)
        {
            _interruptStateHandle.Free();
        }
    }

    private bool EnsureBgraOutputFrame(AVPixelFormat sourceFormat, int width, int height)
    {
        if (_bgraFrame is null)
        {
            LastError = "FFmpeg BGRA frame is unavailable.";
            return false;
        }

        if (_swsContext is not null
            && _currentWidth == width
            && _currentHeight == height
            && _currentSourceFormat == sourceFormat)
        {
            return true;
        }

        if (_swsContext is not null)
        {
            ffmpeg.sws_freeContext(_swsContext);
            _swsContext = null;
        }

        ffmpeg.av_frame_unref(_bgraFrame);
        _bgraFrame->format = (int)AVPixelFormat.AV_PIX_FMT_BGRA;
        _bgraFrame->width = width;
        _bgraFrame->height = height;

        var bufferResult = ffmpeg.av_frame_get_buffer(_bgraFrame, 1);
        if (bufferResult < 0)
        {
            LastError = $"FFmpeg could not allocate the BGRA conversion buffer: {GetErrorText(bufferResult)}";
            return false;
        }

        _swsContext = ffmpeg.sws_getCachedContext(
            _swsContext,
            width,
            height,
            sourceFormat,
            width,
            height,
            AVPixelFormat.AV_PIX_FMT_BGRA,
            (int)SwsFlags.SWS_FAST_BILINEAR,
            null,
            null,
            null);

        if (_swsContext is null)
        {
            LastError = "FFmpeg could not create a software scaler context for BGRA output.";
            return false;
        }

        _currentWidth = width;
        _currentHeight = height;
        _currentSourceFormat = sourceFormat;
        return true;
    }

    private static void SetOption(AVDictionary** options, string key, string value)
    {
        ffmpeg.av_dict_set(options, key, value, 0);
    }

    private static string GetErrorText(int errorCode)
    {
        const int errorBufferSize = 1024;
        var errorBuffer = stackalloc byte[errorBufferSize];
        ffmpeg.av_strerror(errorCode, errorBuffer, (ulong)errorBufferSize);
        return Marshal.PtrToStringUTF8((IntPtr)errorBuffer) ?? $"FFmpeg error {errorCode}";
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static int InterruptCallback(void* opaque)
    {
        var handle = GCHandle.FromIntPtr((IntPtr)opaque);
        if (!handle.IsAllocated || handle.Target is not InterruptState state)
        {
            return 0;
        }

        if (state.CancelRequested)
        {
            return 1;
        }

        if (state.ReadDeadlineTickCount != long.MaxValue && Environment.TickCount64 >= state.ReadDeadlineTickCount)
        {
            state.ReadTimedOut = true;
            return 1;
        }

        return 0;
    }

    private sealed class InterruptState
    {
        public volatile bool CancelRequested;
        public volatile bool ReadTimedOut;
        public long ReadDeadlineTickCount = long.MaxValue;
    }
}

public sealed class DecodedVideoFrame : IDisposable
{
    private static readonly ConcurrentBag<DecodedVideoFrame> Pool = new();

    private byte[]? _buffer;
    private int _bufferLength;
    private int _width;
    private int _height;
    private int _stride;

    private DecodedVideoFrame()
    {
    }

    public static DecodedVideoFrame Rent(byte[] buffer, int bufferLength, int width, int height, int stride)
    {
        var frame = Pool.TryTake(out var pooledFrame)
            ? pooledFrame
            : new DecodedVideoFrame();

        frame._buffer = buffer;
        frame._bufferLength = bufferLength;
        frame._width = width;
        frame._height = height;
        frame._stride = stride;
        return frame;
    }

    public byte[] Buffer => _buffer ?? throw new ObjectDisposedException(nameof(DecodedVideoFrame));

    public int BufferLength => _bufferLength;

    public int Width => _width;

    public int Height => _height;

    public int Stride => _stride;

    public void Dispose()
    {
        var buffer = Interlocked.Exchange(ref _buffer, null);
        if (buffer is not null)
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        _bufferLength = 0;
        _width = 0;
        _height = 0;
        _stride = 0;
        Pool.Add(this);
    }
}