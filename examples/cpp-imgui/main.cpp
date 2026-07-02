#define WIN32_LEAN_AND_MEAN

#include "gua/imgui_adapter.hpp"
#include "gua/ws_bridge.hpp"

#include "imgui.h"
#include "imgui_impl_dx11.h"
#include "imgui_impl_win32.h"

#include <d3d11.h>
#include <tchar.h>
#include <windows.h>

#include <cstdlib>
#include <iostream>
#include <mutex>
#include <sstream>
#include <string>
#include <string_view>

namespace {

ID3D11Device* g_pd3dDevice = nullptr;
ID3D11DeviceContext* g_pd3dDeviceContext = nullptr;
IDXGISwapChain* g_pSwapChain = nullptr;
ID3D11RenderTargetView* g_mainRenderTargetView = nullptr;

std::string PercentEncode(std::string_view value)
{
    constexpr char hex[] = "0123456789ABCDEF";
    std::string out;
    out.reserve(value.size());
    for (const unsigned char ch : value) {
        if ((ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9')
            || ch == '-' || ch == '_' || ch == '.' || ch == '~') {
            out.push_back(static_cast<char>(ch));
        } else {
            out.push_back('%');
            out.push_back(hex[(ch >> 4U) & 0x0fU]);
            out.push_back(hex[ch & 0x0fU]);
        }
    }
    return out;
}

std::string RenderInspectorSvg(bool loading)
{
    const std::string title = loading ? "Loading..." : "Gua ImGui Runtime";
    const std::string subtitle = loading ? "Start command was received by the ImGui sample." : "Connected directly to examples/cpp-imgui.";
    std::ostringstream svg;
    svg << "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1280\" height=\"720\" viewBox=\"0 0 1280 720\">"
        << "<rect width=\"1280\" height=\"720\" fill=\"#101820\"/>"
        << "<rect x=\"448\" y=\"232\" width=\"384\" height=\"256\" fill=\"#1f2937\" stroke=\"#4b647f\" stroke-width=\"2\"/>"
        << "<text x=\"640\" y=\"284\" fill=\"#e8edf4\" font-family=\"Segoe UI, sans-serif\" font-size=\"34\" text-anchor=\"middle\">" << title << "</text>"
        << "<text x=\"640\" y=\"520\" fill=\"#91a4b7\" font-family=\"Segoe UI, sans-serif\" font-size=\"20\" text-anchor=\"middle\">" << subtitle << "</text>";

    if (!loading) {
        svg << "<rect x=\"512\" y=\"312\" width=\"256\" height=\"56\" fill=\"#253448\" stroke=\"#f2c66d\" stroke-width=\"2\"/>"
            << "<text x=\"640\" y=\"348\" fill=\"#f5f7fb\" font-family=\"Segoe UI, sans-serif\" font-size=\"22\" text-anchor=\"middle\">Start Game</text>";
    }

    svg << "</svg>";
    return svg.str();
}

void CreateRenderTarget()
{
    ID3D11Texture2D* back_buffer = nullptr;
    g_pSwapChain->GetBuffer(0, IID_PPV_ARGS(&back_buffer));
    g_pd3dDevice->CreateRenderTargetView(back_buffer, nullptr, &g_mainRenderTargetView);
    back_buffer->Release();
}

void CleanupRenderTarget()
{
    if (g_mainRenderTargetView != nullptr) {
        g_mainRenderTargetView->Release();
        g_mainRenderTargetView = nullptr;
    }
}

bool CreateDeviceD3D(HWND window)
{
    DXGI_SWAP_CHAIN_DESC swap_chain_desc {};
    swap_chain_desc.BufferCount = 2;
    swap_chain_desc.BufferDesc.Width = 0;
    swap_chain_desc.BufferDesc.Height = 0;
    swap_chain_desc.BufferDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    swap_chain_desc.BufferDesc.RefreshRate.Numerator = 60;
    swap_chain_desc.BufferDesc.RefreshRate.Denominator = 1;
    swap_chain_desc.Flags = DXGI_SWAP_CHAIN_FLAG_ALLOW_MODE_SWITCH;
    swap_chain_desc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    swap_chain_desc.OutputWindow = window;
    swap_chain_desc.SampleDesc.Count = 1;
    swap_chain_desc.SampleDesc.Quality = 0;
    swap_chain_desc.Windowed = TRUE;
    swap_chain_desc.SwapEffect = DXGI_SWAP_EFFECT_DISCARD;

    constexpr D3D_FEATURE_LEVEL feature_levels[] = {
        D3D_FEATURE_LEVEL_11_0,
        D3D_FEATURE_LEVEL_10_0,
    };
    D3D_FEATURE_LEVEL selected_feature_level {};

    const HRESULT result = D3D11CreateDeviceAndSwapChain(
        nullptr,
        D3D_DRIVER_TYPE_HARDWARE,
        nullptr,
        0,
        feature_levels,
        2,
        D3D11_SDK_VERSION,
        &swap_chain_desc,
        &g_pSwapChain,
        &g_pd3dDevice,
        &selected_feature_level,
        &g_pd3dDeviceContext);

    if (result == DXGI_ERROR_UNSUPPORTED) {
        return SUCCEEDED(D3D11CreateDeviceAndSwapChain(
            nullptr,
            D3D_DRIVER_TYPE_WARP,
            nullptr,
            0,
            feature_levels,
            2,
            D3D11_SDK_VERSION,
            &swap_chain_desc,
            &g_pSwapChain,
            &g_pd3dDevice,
            &selected_feature_level,
            &g_pd3dDeviceContext));
    }

    if (FAILED(result)) {
        return false;
    }

    CreateRenderTarget();
    return true;
}

void CleanupDeviceD3D()
{
    CleanupRenderTarget();
    if (g_pSwapChain != nullptr) {
        g_pSwapChain->Release();
        g_pSwapChain = nullptr;
    }
    if (g_pd3dDeviceContext != nullptr) {
        g_pd3dDeviceContext->Release();
        g_pd3dDeviceContext = nullptr;
    }
    if (g_pd3dDevice != nullptr) {
        g_pd3dDevice->Release();
        g_pd3dDevice = nullptr;
    }
}

void DumpEvents(gua::Context& context)
{
    gua::Event event;
    while (context.poll_event(event)) {
        if (event.type == gua::EventType::click) {
            std::cout << "click:" << event.node_id << '\n';
        }
    }
}

int RunSmoke()
{
    gua::Context context;
    context.begin_frame("title");
    context.log(gua::LogLevel::info, "title screen opened");
    GuaImGui::Button(
        context,
        "start",
        "Start Game",
        GuaImGui::Rect { 100.0F, 200.0F, 240.0F, 64.0F },
        true);
    context.set_screenshot("data:image/gif;base64,R0lGODlhAQABAAAAACw=", 1, 1);
    context.end_frame();

    std::cout << context.ui_tree_json() << '\n';
    std::cout << context.logs_json() << '\n';
    std::cout << context.screenshot_json() << '\n';
    DumpEvents(context);
    return EXIT_SUCCESS;
}

} // namespace

extern IMGUI_IMPL_API LRESULT ImGui_ImplWin32_WndProcHandler(HWND hwnd, UINT msg, WPARAM wparam, LPARAM lparam);

LRESULT WINAPI WndProc(HWND hwnd, UINT msg, WPARAM wparam, LPARAM lparam)
{
    if (ImGui_ImplWin32_WndProcHandler(hwnd, msg, wparam, lparam) != 0) {
        return true;
    }

    switch (msg) {
    case WM_SIZE:
        if (wparam != SIZE_MINIMIZED && g_pd3dDevice != nullptr) {
            CleanupRenderTarget();
            g_pSwapChain->ResizeBuffers(0, LOWORD(lparam), HIWORD(lparam), DXGI_FORMAT_UNKNOWN, 0);
            CreateRenderTarget();
        }
        return 0;
    case WM_SYSCOMMAND:
        if ((wparam & 0xfff0U) == SC_KEYMENU) {
            return 0;
        }
        break;
    case WM_DESTROY:
        PostQuitMessage(0);
        return 0;
    default:
        break;
    }

    return DefWindowProcW(hwnd, msg, wparam, lparam);
}

int main(int argc, char** argv)
{
    if (argc > 1 && std::string_view(argv[1]) == "--smoke") {
        return RunSmoke();
    }

    WNDCLASSEXW window_class {
        sizeof(WNDCLASSEXW),
        CS_CLASSDC,
        WndProc,
        0,
        0,
        GetModuleHandleW(nullptr),
        nullptr,
        nullptr,
        nullptr,
        nullptr,
        L"GuaImGuiExample",
        nullptr,
    };
    RegisterClassExW(&window_class);

    HWND window = CreateWindowW(
        window_class.lpszClassName,
        L"Gua ImGui Win32 DirectX11 Example",
        WS_OVERLAPPEDWINDOW,
        100,
        100,
        1280,
        800,
        nullptr,
        nullptr,
        window_class.hInstance,
        nullptr);

    if (!CreateDeviceD3D(window)) {
        CleanupDeviceD3D();
        UnregisterClassW(window_class.lpszClassName, window_class.hInstance);
        return EXIT_FAILURE;
    }

    ShowWindow(window, SW_SHOWDEFAULT);
    UpdateWindow(window);

    IMGUI_CHECKVERSION();
    ImGui::CreateContext();
    ImGuiIO& io = ImGui::GetIO();
    io.ConfigFlags |= ImGuiConfigFlags_NavEnableKeyboard;
    io.ConfigFlags |= ImGuiConfigFlags_NavEnableGamepad;

    ImGui::StyleColorsDark();
    ImGui_ImplWin32_Init(window);
    ImGui_ImplDX11_Init(g_pd3dDevice, g_pd3dDeviceContext);

    gua::Context gua_context;
    std::mutex gua_mutex;
    gua_context.log(gua::LogLevel::info, "cpp-imgui runtime started");

    gua::ws::BridgeServer bridge(
        gua::ws::BridgeHandlers {
            .get_ui_tree_json = [&] {
                const std::lock_guard lock(gua_mutex);
                return gua_context.ui_tree_json();
            },
            .get_logs_json = [&] {
                const std::lock_guard lock(gua_mutex);
                return gua_context.logs_json();
            },
            .get_screenshot_json = [&] {
                const std::lock_guard lock(gua_mutex);
                return gua_context.screenshot_json();
            },
            .click_node = [&](std::string_view node_id) {
                const std::lock_guard lock(gua_mutex);
                return gua_context.enqueue_click(node_id);
            },
            .focus_node = [&](std::string_view node_id) {
                const std::lock_guard lock(gua_mutex);
                std::string id(node_id);
                char found[128] {};
                if (gua_find_node_by_id(gua_context.native_handle(), id.c_str(), found, static_cast<int>(sizeof(found))) == 0) {
                    return false;
                }
                gua_context.log(gua::LogLevel::debug, "focus_node(" + id + ")");
                return true;
            },
            .press_key = [&](std::string_view key) {
                const std::lock_guard lock(gua_mutex);
                gua_context.log(gua::LogLevel::info, "press_key(" + std::string(key) + ")");
                return !key.empty();
            },
        },
        gua::ws::BridgeOptions { .port = 8765 });
    bridge.start();

    bool show_demo_window = false;
    bool loading = false;
    bool done = false;

    while (!done) {
        MSG msg {};
        while (PeekMessageW(&msg, nullptr, 0U, 0U, PM_REMOVE) != 0) {
            TranslateMessage(&msg);
            DispatchMessageW(&msg);
            if (msg.message == WM_QUIT) {
                done = true;
            }
        }
        if (done) {
            break;
        }

        ImGui_ImplDX11_NewFrame();
        ImGui_ImplWin32_NewFrame();
        ImGui::NewFrame();

        {
            const std::lock_guard lock(gua_mutex);
            gua_context.begin_frame(loading ? "loading" : "title");
            ImGui::Begin("Gua Runtime UI");
            ImGui::TextUnformatted("This window behaves like a small in-game UI surface.");
            ImGui::TextUnformatted("Inspector bridge: ws://127.0.0.1:8765");
            ImGui::Separator();

            if (!loading) {
                GuaImGui::Button(gua_context, "start", "Start Game");
            } else {
                ImGui::TextUnformatted("Loading...");
                const ImVec2 min = ImGui::GetItemRectMin();
                const ImVec2 max = ImGui::GetItemRectMax();
                gua_context.text("loading", "Loading...", gua::Rect { min.x, min.y, max.x - min.x, max.y - min.y });
            }

            if (ImGui::Button("Dump UI Tree")) {
                std::cout << gua_context.ui_tree_json() << '\n';
            }
            ImGui::Checkbox("Show ImGui demo", &show_demo_window);
            ImGui::End();

            gua_context.set_screenshot("data:image/svg+xml," + PercentEncode(RenderInspectorSvg(loading)), 1280, 720);
            gua_context.end_frame();
        }

        if (show_demo_window) {
            ImGui::ShowDemoWindow(&show_demo_window);
        }

        {
            const std::lock_guard lock(gua_mutex);
            gua::Event event;
            while (gua_context.poll_event(event)) {
                std::cout << "click:" << event.node_id << '\n';
                if (event.type == gua::EventType::click && event.node_id == "start") {
                    gua_context.log(gua::LogLevel::info, "start button clicked");
                    loading = true;
                }
            }
        }
        bridge.publish_snapshot();

        ImGui::Render();
        constexpr float clear_color[4] = { 0.07F, 0.08F, 0.10F, 1.00F };
        g_pd3dDeviceContext->OMSetRenderTargets(1, &g_mainRenderTargetView, nullptr);
        g_pd3dDeviceContext->ClearRenderTargetView(g_mainRenderTargetView, clear_color);
        ImGui_ImplDX11_RenderDrawData(ImGui::GetDrawData());

        g_pSwapChain->Present(1, 0);
    }

    ImGui_ImplDX11_Shutdown();
    ImGui_ImplWin32_Shutdown();
    ImGui::DestroyContext();

    CleanupDeviceD3D();
    DestroyWindow(window);
    UnregisterClassW(window_class.lpszClassName, window_class.hInstance);

    return EXIT_SUCCESS;
}
