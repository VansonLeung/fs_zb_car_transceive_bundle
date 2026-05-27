cmake_minimum_required(VERSION 3.20)
project(fpv4win_bridge LANGUAGES C CXX)

set(CMAKE_C_STANDARD 11)
set(CMAKE_C_STANDARD_REQUIRED ON)
set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

set(
    FPV4WIN_SOURCE_ROOT
    "${CMAKE_CURRENT_LIST_DIR}/../../../__references/fpv4win-main/fpv4win-main"
    CACHE PATH
    "Path to fpv4win source tree"
)

set(
    RTL_SOURCE_ROOT
    "${FPV4WIN_SOURCE_ROOT}/3rd/rtl8812au-monitor-pcap"
    CACHE PATH
    "Path to rtl8812au-monitor-pcap source tree"
)

if(NOT EXISTS "${FPV4WIN_SOURCE_ROOT}/src/wifi/WFBProcessor.cpp")
    message(FATAL_ERROR "WFBProcessor.cpp not found under FPV4WIN_SOURCE_ROOT: ${FPV4WIN_SOURCE_ROOT}")
endif()

if(NOT EXISTS "${RTL_SOURCE_ROOT}/src/WiFiDriver.cpp")
    message(FATAL_ERROR "WiFiDriver.cpp not found under RTL_SOURCE_ROOT: ${RTL_SOURCE_ROOT}")
endif()

find_package(PkgConfig REQUIRED)
find_package(unofficial-sodium CONFIG REQUIRED)
pkg_check_modules(LIBUSB REQUIRED IMPORTED_TARGET libusb-1.0)

file(GLOB RTL_CPP_SOURCES CONFIGURE_DEPENDS "${RTL_SOURCE_ROOT}/src/*.cpp")
file(GLOB RTL_C_SOURCES CONFIGURE_DEPENDS "${RTL_SOURCE_ROOT}/hal/*.c")
list(FILTER RTL_CPP_SOURCES EXCLUDE REGEX ".*/src/main.cpp$")

set(BRIDGE_SOURCES
    src/fpv4win_bridge.cpp
    ${FPV4WIN_SOURCE_ROOT}/src/wifi/WFBProcessor.cpp
    ${FPV4WIN_SOURCE_ROOT}/src/wifi/fec.c
    ${RTL_CPP_SOURCES}
    ${RTL_C_SOURCES}
)

add_library(fpv4win_bridge SHARED ${BRIDGE_SOURCES})

target_include_directories(fpv4win_bridge
    PRIVATE
        include
        ${FPV4WIN_SOURCE_ROOT}/src
        ${FPV4WIN_SOURCE_ROOT}/src/wifi
        ${RTL_SOURCE_ROOT}/src
        ${RTL_SOURCE_ROOT}/hal
)

target_compile_definitions(fpv4win_bridge PRIVATE FPV4WIN_BRIDGE_BUILDING _CRT_SECURE_NO_WARNINGS)

if(MSVC)
    target_compile_options(fpv4win_bridge PRIVATE /W3 /EHsc /Zc:__cplusplus)
else()
    target_compile_options(fpv4win_bridge PRIVATE -Wall -Wextra -Wpedantic)
endif()

target_link_libraries(fpv4win_bridge PRIVATE PkgConfig::LIBUSB unofficial-sodium::sodium)

if(WIN32)
    target_link_libraries(fpv4win_bridge PRIVATE ws2_32)
endif()

set_target_properties(fpv4win_bridge PROPERTIES OUTPUT_NAME "fpv4win_bridge")
