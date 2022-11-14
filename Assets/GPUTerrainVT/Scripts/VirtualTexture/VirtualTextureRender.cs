using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;


namespace RVT
{
    internal unsafe class VirtualTextureFeedback
    {
        internal bool isReady;
        internal NativeArray<Color32> readbackDatas;

        public VirtualTextureFeedback(in bool bReady)
        {
            isReady = bReady;
        }

        internal void RequestReadback(CommandBuffer cmdBuffer, RenderTexture feedbackTexture)
        {
            isReady = false;
            cmdBuffer.RequestAsyncReadback(feedbackTexture, 0, feedbackTexture.graphicsFormat, EnqueueCopy);
        }

        private void EnqueueCopy(AsyncGPUReadbackRequest request)
        {
            if (request.hasError || request.done == true)
            {
                isReady = true;
                readbackDatas = request.GetData<Color32>();
            }
        }
    }

    internal class VirtualTextureRenderPass : ScriptableRenderPass
    {
        int m_FeedbackScale;
        RenderTexture m_FeedbackTexture;
        RenderTargetIdentifier m_FeedbackTextureID;
        VirtualTextureFeedback m_FeedbackProcessor;

        internal static Action<ScriptableRenderContext, CameraData, int> s_ExecuteAction;

        public VirtualTextureRenderPass(int feedbackScale)
        {
            m_FeedbackScale = feedbackScale;
            m_FeedbackProcessor = new VirtualTextureFeedback(true);
        }
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            Camera camera = renderingData.cameraData.camera;
            float scale = renderingData.cameraData.renderScale / m_FeedbackScale;

            m_FeedbackTexture = RenderTexture.GetTemporary((int)(camera.pixelWidth * scale), (int)(camera.pixelHeight * scale), 16, GraphicsFormat.R8G8B8A8_UNorm);
            m_FeedbackTexture.name = "FeedbackTexture";
            m_FeedbackTextureID = new RenderTargetIdentifier(m_FeedbackTexture);
        }

        public override void Execute(ScriptableRenderContext renderContext, ref RenderingData renderingData)
        {
            if (!Application.isPlaying || !VirtualTexture.s_VirtualTexture) { return; }

            CommandBuffer cmdBuffer = CommandBufferPool.Get();
            CameraData cameraData = renderingData.cameraData;

            DrawVirtualTexture(renderContext, cmdBuffer, cameraData);

            renderContext.ExecuteCommandBuffer(cmdBuffer);
            CommandBufferPool.Release(cmdBuffer);
        }

        public unsafe void DrawVirtualTexture(ScriptableRenderContext renderContext, CommandBuffer cmdBuffer, CameraData cameraData)
        {
            Camera camera = cameraData.camera;
            VirtualTexture virtualTexture = VirtualTexture.s_VirtualTexture;
            PageProducer pageProducer = virtualTexture.pageProducer;
            PageRenderer pageRenderer = virtualTexture.pageRenderer;
            if (m_FeedbackProcessor.isReady)
            {
                NativeArray<int4> decodeDatas = new NativeArray<int4>(m_FeedbackProcessor.readbackDatas.Length, Allocator.TempJob);
                DecodeFeedbackJob decodeFeabackJob;
                decodeFeabackJob.pageSize = virtualTexture.pageSize;
                decodeFeabackJob.decodeDatas = decodeDatas;
                decodeFeabackJob.encodeDatas = m_FeedbackProcessor.readbackDatas;
                decodeFeabackJob.Schedule(m_FeedbackProcessor.readbackDatas.Length, 256).Complete();

                pageProducer.ProcessFeedback(ref decodeDatas, virtualTexture.NumMip, virtualTexture.tileNum, virtualTexture.pageSize, virtualTexture.lruCache, ref pageRenderer.loadRequests);
                decodeDatas.Dispose();

                cmdBuffer.SetRenderTarget(virtualTexture.tableTextureID);
                pageRenderer.DrawPageTable(renderContext, cmdBuffer, pageProducer);
            }

            {
                cmdBuffer.SetRenderTarget(m_FeedbackTextureID);
                cmdBuffer.ClearRenderTarget(true, true, Color.black);
                // x: 页表大小(单位: 页)
                // y: 虚拟贴图大小(单位: 像素)
                // z: 最大mipmap等级
                // w: mipBias
                cmdBuffer.SetGlobalVector("_VTFeedbackParams", new Vector4(virtualTexture.pageSize, virtualTexture.pageSize * virtualTexture.tileSize * (1.0f / (float)m_FeedbackScale), virtualTexture.NumMip, virtualTexture.mipmapBias));
                float cameraAspect = (float)camera.pixelRect.width / (float)camera.pixelRect.height;
                Matrix4x4 projectionMatrix = Matrix4x4.Perspective(90, cameraAspect, camera.nearClipPlane, camera.farClipPlane);
                projectionMatrix = GL.GetGPUProjectionMatrix(projectionMatrix, true);
                RenderingUtils.SetViewAndProjectionMatrices(cmdBuffer, camera.worldToCameraMatrix, projectionMatrix, false);
                renderContext.ExecuteCommandBuffer(cmdBuffer);
                cmdBuffer.Clear();

                s_ExecuteAction?.Invoke(renderContext, cameraData, 0);

                projectionMatrix = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
                RenderingUtils.SetViewAndProjectionMatrices(cmdBuffer, camera.worldToCameraMatrix, projectionMatrix, false);
                renderContext.ExecuteCommandBuffer(cmdBuffer);
                cmdBuffer.Clear();

            }
            // read-back 
            {
                m_FeedbackProcessor.RequestReadback(cmdBuffer, m_FeedbackTexture);
            }

            {
                cmdBuffer.SetRenderTarget(virtualTexture.colorTextureIDs, virtualTexture.colorTextureIDs[0]);
                renderContext.ExecuteCommandBuffer(cmdBuffer);
                cmdBuffer.Clear();
                pageRenderer.DrawPageColor(renderContext, cmdBuffer, pageProducer, virtualTexture, ref virtualTexture.lruCache[0]);
            }
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            RenderTexture.ReleaseTemporary(m_FeedbackTexture);
        }


    }

    public class VirtualTextureRender : ScriptableRendererFeature
    {
        public int feedbackScale = 8;

        VirtualTextureRenderPass m_FeedbackRenderPass;

        public override void Create()
        {
            m_FeedbackRenderPass = new VirtualTextureRenderPass(feedbackScale);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            renderer.EnqueuePass(m_FeedbackRenderPass);
            m_FeedbackRenderPass.renderPassEvent = RenderPassEvent.BeforeRenderingPrePasses;
        }
    }

    [BurstCompile]
    internal struct DecodeFeedbackJob : IJobParallelFor
    {
        [ReadOnly]
        internal int pageSize;

        [ReadOnly]
        internal NativeArray<Color32> encodeDatas;

        [WriteOnly]
        internal NativeArray<int4> decodeDatas;

        public void Execute(int index)
        {
            Color32 x = encodeDatas[index];
            float4 xRaw = new float4((float)x.r / 255.0f, (float)x.g / 255.0f, (float)x.b / 255.0f, x.a);

            float3 xint = math.floor(xRaw.xyz * 255);
            float2 lbit = xint.xy;
            float2 hbit;
            hbit.y = math.floor(xint.z / 16);
            hbit.x = xint.z - hbit.y * 16;
            float2 pageUV = lbit + hbit * 256;
            decodeDatas[index] = new int4((int)pageUV.x, (int)pageUV.y, x.a, 255);
        }
    }
}