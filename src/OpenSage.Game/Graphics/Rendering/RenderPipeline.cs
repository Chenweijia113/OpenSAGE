﻿using System.Numerics;
using System.Runtime.InteropServices;
using OpenSage.Data.Map;
using OpenSage.Graphics.Effects;
using OpenSage.Gui;
using OpenSage.Mathematics;
using Veldrid;

namespace OpenSage.Graphics.Rendering
{
    internal sealed class RenderPipeline : DisposableBase
    {
        public static readonly OutputDescription GameOutputDescription = new OutputDescription(
            new OutputAttachmentDescription(PixelFormat.D32_Float_S8_UInt),
            new OutputAttachmentDescription(PixelFormat.B8_G8_R8_A8_UNorm));

        public const PixelFormat ShadowMapPixelFormat = PixelFormat.D32_Float_S8_UInt;

        public static readonly OutputDescription DepthPassDescription = new OutputDescription(
            new OutputAttachmentDescription(ShadowMapPixelFormat));

        private readonly RenderList _renderList;

        private readonly CommandList _commandList;

        private readonly ConstantBuffer<GlobalConstantsShared> _globalConstantBufferShared;
        private readonly ConstantBuffer<GlobalConstantsVS> _globalConstantBufferVS;
        private readonly ConstantBuffer<GlobalConstantsPS> _globalConstantBufferPS;
        private readonly ConstantBuffer<RenderItemConstantsVS> _renderItemConstantsBufferVS;
        private readonly ConstantBuffer<LightingConstantsVS> _globalLightingVSTerrainBuffer;
        private readonly ConstantBuffer<LightingConstantsPS> _globalLightingPSTerrainBuffer;
        private readonly ConstantBuffer<LightingConstantsVS> _globalLightingVSObjectBuffer;
        private readonly ConstantBuffer<LightingConstantsPS> _globalLightingPSObjectBuffer;

        private readonly DrawingContext2D _drawingContext;

        private readonly Framebuffer _shadowMapFramebuffer;

        public RenderPipeline(Game game)
        {
            _renderList = new RenderList();

            var graphicsDevice = game.GraphicsDevice;

            _globalConstantBufferShared = AddDisposable(new ConstantBuffer<GlobalConstantsShared>(graphicsDevice, "GlobalConstantsShared"));
            _globalConstantBufferVS = AddDisposable(new ConstantBuffer<GlobalConstantsVS>(graphicsDevice, "GlobalConstantsVS"));
            _renderItemConstantsBufferVS = AddDisposable(new ConstantBuffer<RenderItemConstantsVS>(graphicsDevice, "RenderItemConstantsVS"));
            _globalConstantBufferPS = AddDisposable(new ConstantBuffer<GlobalConstantsPS>(graphicsDevice, "GlobalConstantsPS"));
            _globalLightingVSTerrainBuffer = AddDisposable(new ConstantBuffer<LightingConstantsVS>(graphicsDevice, "Global_LightingConstantsVS (terrain)"));
            _globalLightingPSTerrainBuffer = AddDisposable(new ConstantBuffer<LightingConstantsPS>(graphicsDevice, "Global_LightingConstantsPS (terrain)"));
            _globalLightingVSObjectBuffer = AddDisposable(new ConstantBuffer<LightingConstantsVS>(graphicsDevice, "Global_LightingConstantsVS (objects)"));
            _globalLightingPSObjectBuffer = AddDisposable(new ConstantBuffer<LightingConstantsPS>(graphicsDevice, "Global_LightingConstantsPS (objects)"));

            _commandList = AddDisposable(graphicsDevice.ResourceFactory.CreateCommandList());

            _drawingContext = AddDisposable(new DrawingContext2D(
                game.ContentManager,
                BlendStateDescription.SingleAlphaBlend,
                GameOutputDescription));

            const int shadowMapSize = 1024;
            const int numCascades = 4;
            var depthTexture = AddDisposable(graphicsDevice.ResourceFactory.CreateTexture(
                TextureDescription.Texture2D(
                    shadowMapSize,
                    shadowMapSize,
                    1,
                    numCascades,
                    ShadowMapPixelFormat,
                    TextureUsage.DepthStencil | TextureUsage.Sampled)));

            _shadowMapFramebuffer = AddDisposable(graphicsDevice.ResourceFactory.CreateFramebuffer(
                new FramebufferDescription(depthTexture)));
        }

        public void Execute(RenderContext context)
        {
            _renderList.Clear();

            context.Scene?.BuildRenderList(_renderList, context.Camera);

            foreach (var system in context.Game.GameSystems)
            {
                system.BuildRenderList(_renderList);
            }

            context.Game.RaiseBuildingRenderList(new BuildingRenderListEventArgs(
                _renderList,
                context.Camera));

            var commandEncoder = _commandList;

            commandEncoder.Begin();

            commandEncoder.SetFramebuffer(context.RenderTarget);

            commandEncoder.ClearColorTarget(0, ColorRgba.DimGray.ToColorRgbaF().ToRgbaFloat());
            commandEncoder.ClearDepthStencil(1);

            commandEncoder.SetViewport(0, context.Game.Viewport);

            UpdateGlobalConstantBuffers(commandEncoder, context);

            Texture cloudTexture;
            if (context.Scene != null
                && context.Scene.Lighting.TimeOfDay != TimeOfDay.Night
                && context.Scene.Lighting.EnableCloudShadows
                && context.Scene.Terrain != null)
            {
                cloudTexture = context.Scene.Terrain.CloudTexture;
            }
            else
            {
                cloudTexture = context.Game.ContentManager.SolidWhiteTexture;
            }

            void doRenderPass(RenderBucket bucket)
            {
                Culler.Cull(bucket.RenderItems, bucket.CulledItems, context);

                if (bucket.CulledItems.Count == 0)
                {
                    return;
                }

                bucket.CulledItems.Sort();

                RenderItem? lastRenderItem = null;
                Matrix4x4? lastWorld = null;

                foreach (var renderItem in bucket.CulledItems)
                {
                    if (lastRenderItem == null || lastRenderItem.Value.Effect != renderItem.Effect)
                    {
                        var effect = renderItem.Effect;

                        effect.Begin(commandEncoder);

                        SetDefaultConstantBuffers(renderItem.Material);

                        var cloudTextureParameter = renderItem.Effect.GetParameter("Global_CloudTexture", throwIfMissing: false);
                        if (cloudTextureParameter != null)
                        {
                            renderItem.Material.SetProperty("Global_CloudTexture", cloudTexture);
                        }
                    }

                    if (lastRenderItem == null || lastRenderItem.Value.Material != renderItem.Material)
                    {
                        renderItem.Material.ApplyPipelineState();
                        renderItem.Effect.ApplyPipelineState(commandEncoder);
                    }

                    if (lastRenderItem == null || lastRenderItem.Value.VertexBuffer0 != renderItem.VertexBuffer0)
                    {
                        commandEncoder.SetVertexBuffer(0, renderItem.VertexBuffer0);
                    }

                    if (lastRenderItem == null || lastRenderItem.Value.VertexBuffer1 != renderItem.VertexBuffer1)
                    {
                        if (renderItem.VertexBuffer1 != null)
                        {
                            commandEncoder.SetVertexBuffer(1, renderItem.VertexBuffer1);
                        }
                    }

                    var renderItemConstantsVSParameter = renderItem.Effect.GetParameter("RenderItemConstantsVS");
                    if (renderItemConstantsVSParameter != null)
                    {
                        if (lastWorld == null || lastWorld.Value != renderItem.World)
                        {
                            _renderItemConstantsBufferVS.Value.World = renderItem.World;
                            _renderItemConstantsBufferVS.Update(commandEncoder);

                            lastWorld = renderItem.World;
                        }

                        renderItem.Material.SetProperty(
                            "RenderItemConstantsVS",
                            _renderItemConstantsBufferVS.Buffer);
                    }

                    renderItem.Material.ApplyProperties();
                    renderItem.Effect.ApplyParameters(commandEncoder);

                    switch (renderItem.DrawCommand)
                    {
                        case DrawCommand.Draw:
                            commandEncoder.Draw(
                                renderItem.VertexCount,
                                1,
                                renderItem.VertexStart,
                                0);
                            break;

                        case DrawCommand.DrawIndexed:
                            commandEncoder.SetIndexBuffer(renderItem.IndexBuffer, IndexFormat.UInt16);
                            commandEncoder.DrawIndexed(
                                renderItem.IndexCount,
                                1,
                                renderItem.StartIndex,
                                0,
                                0);
                            break;

                        default:
                            throw new System.Exception();
                    }

                    lastRenderItem = renderItem;
                }
            }

            doRenderPass(_renderList.Opaque);
            doRenderPass(_renderList.Transparent);

            // GUI and camera-dependent 2D elements
            {
                _drawingContext.Begin(
                    commandEncoder,
                    context.Game.ContentManager.LinearClampSampler,
                    new SizeF(context.Game.Viewport.Width, context.Game.Viewport.Height));

                context.Game.Scene3D?.Render(_drawingContext);
                context.Game.Scene2D.Render(_drawingContext);

                context.Game.RaiseRendering2D(new Rendering2DEventArgs(_drawingContext));

                _drawingContext.End();
            }

            commandEncoder.End();

            context.GraphicsDevice.SubmitCommands(commandEncoder);

            context.GraphicsDevice.SwapBuffers();
        }

        private void SetDefaultConstantBuffers(EffectMaterial material)
        {
            void setDefaultConstantBuffer(string name, DeviceBuffer buffer)
            {
                var parameter = material.Effect.GetParameter(name, throwIfMissing: false);
                if (parameter != null)
                {
                    material.SetProperty(name, buffer);
                }
            }

            setDefaultConstantBuffer("GlobalConstantsShared", _globalConstantBufferShared.Buffer);
            setDefaultConstantBuffer("GlobalConstantsVS", _globalConstantBufferVS.Buffer);
            setDefaultConstantBuffer("GlobalConstantsPS", _globalConstantBufferPS.Buffer);

            switch (material.LightingType)
            {
                case LightingType.Terrain:
                    setDefaultConstantBuffer("Global_LightingConstantsVS", _globalLightingVSTerrainBuffer.Buffer);
                    setDefaultConstantBuffer("Global_LightingConstantsPS", _globalLightingPSTerrainBuffer.Buffer);
                    break;

                case LightingType.Object:
                    setDefaultConstantBuffer("Global_LightingConstantsVS", _globalLightingVSObjectBuffer.Buffer);
                    setDefaultConstantBuffer("Global_LightingConstantsPS", _globalLightingPSObjectBuffer.Buffer);
                    break;
            }
        }

        private void UpdateGlobalConstantBuffers(CommandList commandEncoder, RenderContext context)
        {
            var cloudShadowView = Matrix4x4.CreateLookAt(
                Vector3.Zero,
                Vector3.Normalize(new Vector3(0, 0.2f, -1)),
                Vector3.UnitY);

            var cloudShadowProjection = Matrix4x4.CreateOrthographic(1, 1, 0, 1);

            var lightingConstantsVS = new LightingConstantsVS
            {
                CloudShadowMatrix = cloudShadowView * cloudShadowProjection
            };

            if (context.Scene != null)
            {
                var cameraPosition = Matrix4x4Utility.Invert(context.Camera.View).Translation;

                _globalConstantBufferShared.Value.CameraPosition = cameraPosition;
                _globalConstantBufferShared.Value.TimeInSeconds = (float) context.GameTime.TotalGameTime.TotalSeconds;
                _globalConstantBufferShared.Update(commandEncoder);

                _globalConstantBufferVS.Value.ViewProjection = context.Camera.View * context.Camera.Projection;
                _globalConstantBufferVS.Update(commandEncoder);

                void updateLightingBuffer(
                    ConstantBuffer<LightingConstantsVS> bufferVS,
                    ConstantBuffer<LightingConstantsPS> bufferPS,
                    in LightingConstantsPS constantsPS)
                {
                    bufferVS.Value = lightingConstantsVS;
                    bufferVS.Update(commandEncoder);

                    bufferPS.Value = constantsPS;
                    bufferPS.Update(commandEncoder);
                }

                updateLightingBuffer(
                    _globalLightingVSTerrainBuffer,
                    _globalLightingPSTerrainBuffer,
                    context.Scene.Lighting.CurrentLightingConfiguration.TerrainLightsPS);

                updateLightingBuffer(
                    _globalLightingVSObjectBuffer,
                    _globalLightingPSObjectBuffer,
                    context.Scene.Lighting.CurrentLightingConfiguration.ObjectLightsPS);
            }

            _globalConstantBufferPS.Value.ViewportSize = new Vector2(context.Game.Viewport.Width, context.Game.Viewport.Height);
            _globalConstantBufferPS.Update(commandEncoder);
        }

        [StructLayout(LayoutKind.Sequential, Size = 16)]
        private struct GlobalConstantsShared
        {
            public Vector3 CameraPosition;
            public float TimeInSeconds;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct GlobalConstantsVS
        {
            public Matrix4x4 ViewProjection;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RenderItemConstantsVS
        {
            public Matrix4x4 World;
        }

        [StructLayout(LayoutKind.Sequential, Size = 16)]
        private struct GlobalConstantsPS
        {
            public Vector2 ViewportSize;
        }
    }
}
