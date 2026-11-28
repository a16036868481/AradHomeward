// Copyright (c) 2026 eirros. All rights reserved.
#pragma once
#include <windows.h>
#include <unknwn.h>
#include <d3d9types.h>

namespace AradPixelPresentation
{
    // D3D8's COM interface and presentation structures differ from D3D9.
    // Use the D3D8 slots here with the fixed-function constants shared by both.
    enum DeviceMethod : size_t
    {
        CreateAdditionalSwapChain = 13, Reset = 14, Present = 15, GetBackBuffer = 16,
        CreateTexture = 20, CopyRects = 28, SetRenderTarget = 31, GetRenderTarget = 32,
        GetDepthStencilSurface = 33, BeginScene = 34, EndScene = 35, SetViewport = 40,
        SetRenderState = 50, ApplyStateBlock = 54, DeleteStateBlock = 56, CreateStateBlock = 57,
        SetTexture = 61, SetTextureStageState = 63, DrawPrimitiveUP = 72,
        SetVertexShader = 76, SetPixelShader = 88
    };

    struct SwapChainParameters
    {
        UINT width, height;
        D3DFORMAT format;
        UINT backBuffers;
        D3DMULTISAMPLE_TYPE multisampling;
        D3DSWAPEFFECT effect;
        HWND window;
        BOOL windowed, autoDepthStencil;
        D3DFORMAT depthStencilFormat;
        DWORD flags, refreshRate, presentationInterval;
    };

    struct SurfaceDescription
    {
        D3DFORMAT format;
        D3DRESOURCETYPE type;
        DWORD usage;
        D3DPOOL pool;
        UINT size;
        D3DMULTISAMPLE_TYPE multisampling;
        UINT width, height;
    };

    template <size_t slot, typename... Args>
    static HRESULT Call(void* object, Args... args)
    {
        using Method = HRESULT (WINAPI*)(void*, Args...);
        return reinterpret_cast<Method>((*static_cast<void***>(object))[slot])(object, args...);
    }

    static void Release(IUnknown*& object)
    {
        if (object) object->Release();
        object = nullptr;
    }

    struct Surface
    {
        IUnknown* value = nullptr;
        ~Surface() { Release(value); }
    };

    struct Output
    {
        HWND window;
        LONG width, height;
        IUnknown* chain;
        bool unavailable;
    };

    constexpr UINT textureWidth = 1024, textureHeight = 512;
    static void* renderer;
    static IUnknown* texture;
    static IUnknown* textureSurface;
    static Output game, chat;

    static void Clear(Output& output)
    {
        Release(output.chain);
        output = {};
    }

    static void CloseWindow(bool isChat)
    {
        Clear(isChat ? chat : game);
    }

    static void ClearDevice(void* device)
    {
        if (renderer != device) return;
        Clear(game);
        Clear(chat);
        Release(textureSurface);
        Release(texture);
        renderer = nullptr;
    }

    static HRESULT WINAPI ResetDevice(void* device, SwapChainParameters* parameters)
    {
        ClearDevice(device);
        return Call<Reset>(device, parameters);
    }

    static ULONG WINAPI ReleaseDevice(IUnknown* device)
    {
        ClearDevice(device);
        return device->Release();
    }

    static bool Prepare(void* device, HWND window, const RECT& client, Output& output,
        const SurfaceDescription& backBuffer)
    {
        constexpr size_t textureGetSurfaceLevel = 15; // IDirect3DTexture8::GetSurfaceLevel
        if (!textureSurface)
        {
            if (FAILED(Call<CreateTexture>(device, textureWidth, textureHeight, 1u,
                    DWORD{D3DUSAGE_RENDERTARGET}, backBuffer.format, D3DPOOL_DEFAULT, &texture)) ||
                FAILED(Call<textureGetSurfaceLevel>(texture, 0u, &textureSurface)))
            {
                Release(texture);
                return false;
            }
        }
        if (output.chain) return true;
        SwapChainParameters parameters = {};
        parameters.width = client.right;
        parameters.height = client.bottom;
        parameters.format = backBuffer.format;
        parameters.backBuffers = 1;
        parameters.effect = D3DSWAPEFFECT_DISCARD;
        parameters.window = window;
        parameters.windowed = TRUE;
        return SUCCEEDED(Call<CreateAdditionalSwapChain>(device, &parameters, &output.chain));
    }

    static HRESULT DrawPixels(void* device, const RECT& canvas, const RECT& client)
    {
        struct Vertex { float x, y, z, rhw, u, v; };
        const float x = client.right - 0.5f, y = client.bottom - 0.5f;
        const float u = static_cast<float>(canvas.right) / textureWidth;
        const float v = static_cast<float>(canvas.bottom) / textureHeight;
        // Pre-transformed vertices address pixel centers in D3D8.
        const Vertex vertices[] = {
            {-0.5f, -0.5f, 0, 1, 0, 0}, {x, -0.5f, 0, 1, u, 0},
            {-0.5f, y, 0, 1, 0, v}, {x, y, 0, 1, u, v}
        };
        const D3DVIEWPORT9 viewport = {0, 0, static_cast<DWORD>(client.right),
            static_cast<DWORD>(client.bottom), 0, 1};
        Call<SetViewport>(device, &viewport);
        Call<SetVertexShader>(device, DWORD{D3DFVF_XYZRHW | D3DFVF_TEX1});
        Call<SetPixelShader>(device, DWORD{0});
        const D3DRENDERSTATETYPE disabled[] = {D3DRS_ZENABLE, D3DRS_STENCILENABLE, D3DRS_ALPHATESTENABLE,
            D3DRS_ALPHABLENDENABLE, D3DRS_FOGENABLE, D3DRS_CLIPPLANEENABLE};
        for (auto state : disabled) Call<SetRenderState>(device, state, DWORD{0});
        Call<SetRenderState>(device, D3DRS_FILLMODE, D3DFILL_SOLID);
        Call<SetRenderState>(device, D3DRS_CULLMODE, D3DCULL_NONE);
        Call<SetRenderState>(device, D3DRS_COLORWRITEENABLE, DWORD{0xf});
        Call<SetTexture>(device, DWORD{0}, texture);
        Call<SetTextureStageState>(device, DWORD{0}, D3DTSS_COLOROP, D3DTOP_SELECTARG1);
        Call<SetTextureStageState>(device, DWORD{0}, D3DTSS_COLORARG1, DWORD{D3DTA_TEXTURE});
        Call<SetTextureStageState>(device, DWORD{0}, D3DTSS_TEXCOORDINDEX, DWORD{0});
        Call<SetTextureStageState>(device, DWORD{0}, D3DTSS_TEXTURETRANSFORMFLAGS, D3DTTFF_DISABLE);
        Call<SetTextureStageState>(device, DWORD{1}, D3DTSS_COLOROP, D3DTOP_DISABLE);
        // In D3D8 sampler states still belong to the texture stage.
        constexpr DWORD addressU = 13, addressV = 14, magFilter = 16, minFilter = 17, mipFilter = 18;
        Call<SetTextureStageState>(device, DWORD{0}, addressU, D3DTADDRESS_CLAMP);
        Call<SetTextureStageState>(device, DWORD{0}, addressV, D3DTADDRESS_CLAMP);
        Call<SetTextureStageState>(device, DWORD{0}, magFilter, D3DTEXF_POINT);
        Call<SetTextureStageState>(device, DWORD{0}, minFilter, D3DTEXF_POINT);
        Call<SetTextureStageState>(device, DWORD{0}, mipFilter, D3DTEXF_NONE);
        return Call<DrawPrimitiveUP>(device, D3DPT_TRIANGLESTRIP, 2u, static_cast<const void*>(vertices), UINT{sizeof(Vertex)});
    }

    static bool PresentPixels(void* device, HWND window, const RECT& canvas, const RECT& client, Output& output)
    {
        constexpr size_t swapChainPresent = 3, swapChainGetBackBuffer = 4, surfaceGetDesc = 8;
        constexpr auto noRect = static_cast<const RECT*>(nullptr);
        constexpr auto noDirty = static_cast<const RGNDATA*>(nullptr);
        const POINT origin = {0, 0};
        Surface source, target, previous, depth;
        SurfaceDescription description = {};
        if (FAILED(Call<GetBackBuffer>(device, 0u, D3DBACKBUFFER_TYPE_MONO, &source.value)) ||
            FAILED(Call<surfaceGetDesc>(source.value, &description)) ||
            description.width < static_cast<UINT>(canvas.right) ||
            description.height < static_cast<UINT>(canvas.bottom) ||
            !Prepare(device, window, client, output, description) ||
            FAILED(Call<CopyRects>(device, source.value, &canvas, 1u, textureSurface, &origin)) ||
            FAILED(Call<swapChainGetBackBuffer>(output.chain, 0u, D3DBACKBUFFER_TYPE_MONO, &target.value)) ||
            FAILED(Call<GetRenderTarget>(device, &previous.value)))
            return false;

        // Preserve the client's drawing state and both render surfaces.
        Call<GetDepthStencilSurface>(device, &depth.value);
        DWORD saved = 0;
        if (FAILED(Call<CreateStateBlock>(device, D3DSBT_ALL, &saved))) return false;
        bool drew = false;
        if (SUCCEEDED(Call<SetRenderTarget>(device, target.value, static_cast<IUnknown*>(nullptr))))
        {
            if (SUCCEEDED(Call<BeginScene>(device)))
            {
                const HRESULT result = DrawPixels(device, canvas, client);
                drew = SUCCEEDED(Call<EndScene>(device)) && SUCCEEDED(result);
            }
            if (FAILED(Call<SetRenderTarget>(device, previous.value, depth.value))) drew = false;
        }
        if (FAILED(Call<ApplyStateBlock>(device, saved))) drew = false;
        Call<DeleteStateBlock>(device, saved);
        return drew && SUCCEEDED(Call<swapChainPresent>(output.chain, noRect, noRect, HWND{}, noDirty));
    }

    static bool PresentScaled(void* device, HWND window, const RECT& canvas, const RECT& client, bool isChat)
    {
        if (renderer && renderer != device) ClearDevice(renderer);
        renderer = device;
        auto& output = isChat ? chat : game;
        if (output.window != window || output.width != client.right || output.height != client.bottom)
        {
            Clear(output);
            output.window = window;
            output.width = client.right;
            output.height = client.bottom;
        }
        if (output.unavailable) return false;
        if (PresentPixels(device, window, canvas, client, output)) return true;
        Release(output.chain);
        output.unavailable = true;
        return false;
    }
}
