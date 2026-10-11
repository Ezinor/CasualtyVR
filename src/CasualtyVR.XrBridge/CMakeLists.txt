cmake_minimum_required(VERSION 3.24)
project(CasualtyVR.XrBridge LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
# Static C/C++ runtime: no Visual C++ redistributable needed (the game itself has none either).
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
set(OPENXR_SDK "${CMAKE_CURRENT_LIST_DIR}/../../third_party/openxr-1.1.63")

add_library(CasualtyVR.XrBridge SHARED XrBridge.cpp XrBridge.h)
target_include_directories(CasualtyVR.XrBridge PRIVATE "${OPENXR_SDK}/include")
target_link_libraries(CasualtyVR.XrBridge PRIVATE "${OPENXR_SDK}/x64/lib/openxr_loader.lib" d3d11 dxgi)
target_compile_definitions(CasualtyVR.XrBridge PRIVATE UNICODE _UNICODE)
target_compile_options(CasualtyVR.XrBridge PRIVATE /W4 /permissive-)

add_custom_command(TARGET CasualtyVR.XrBridge POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy_if_different "${OPENXR_SDK}/x64/bin/openxr_loader.dll" "$<TARGET_FILE_DIR:CasualtyVR.XrBridge>")
