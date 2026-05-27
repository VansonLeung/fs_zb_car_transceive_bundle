#pragma once

#if defined(_WIN32)
#if defined(FPV4WIN_BRIDGE_BUILDING)
#define FPV4WIN_BRIDGE_API __declspec(dllexport)
#else
#define FPV4WIN_BRIDGE_API __declspec(dllimport)
#endif
#else
#define FPV4WIN_BRIDGE_API
#endif

#ifdef __cplusplus
extern "C" {
#endif

FPV4WIN_BRIDGE_API int fpv4win_bridge_probe(void);
FPV4WIN_BRIDGE_API int fpv4win_bridge_start(
    const char *vidPid,
    int channel,
    int channelWidthIndex,
    const char *keyPath,
    const char *codec,
    int playerPort);
FPV4WIN_BRIDGE_API int fpv4win_bridge_stop(void);
FPV4WIN_BRIDGE_API const char *fpv4win_bridge_get_last_error(void);
FPV4WIN_BRIDGE_API const char *fpv4win_bridge_get_status_json(void);

#ifdef __cplusplus
}
#endif
