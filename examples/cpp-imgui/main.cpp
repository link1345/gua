#define WIN32_LEAN_AND_MEAN

#include "gua/imgui_adapter.hpp"

#include "imgui.h"
#include "imgui_impl_dx11.h"
#include "imgui_impl_win32.h"

#include <d3d11.h>
#include <tchar.h>
#include <windows.h>

#include <cstdlib>
#include <iostream>
#include <string_view>

namespace {

ID3D11Device* g_pd3dDevice = nullptr;
ID3D11DeviceContext* g_pd3dDeviceContext = nullptr;
IDXGISwapChain* g_pSwapChain = nullptr;
ID3D11RenderTargetView* g_mainRenderTargetView = nullptr;

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
    GuaImGui::Button(
        context,
        "start",
        "Start Game",
        GuaImGui::Rect { 100.0F, 200.0F, 240.0F, 64.0F },
        true);
    context.end_frame();

    std::cout << context.ui_tree_json() << '\n';
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

        gua_context.begin_frame(loading ? "loading" : "title");

        ImGui::Begin("Gua Runtime UI");
        ImGui::TextUnformatted("This window behaves like a small in-game UI surface.");
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

        if (show_demo_window) {
            ImGui::ShowDemoWindow(&show_demo_window);
        }

        gua_context.end_frame();

        gua::Event event;
        while (gua_context.poll_event(event)) {
            std::cout << "click:" << event.node_id << '\n';
            if (event.type == gua::EventType::click && event.node_id == "start") {
                loading = true;
            }
        }

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
