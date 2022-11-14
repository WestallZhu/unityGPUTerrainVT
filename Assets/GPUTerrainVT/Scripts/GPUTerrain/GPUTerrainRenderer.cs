using System;
using System.Collections.Generic;
using UnityEngine;

using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;


internal class GPUTerrainRenderer : ScriptableRendererFeature
{
    GPUTerrainPass m_GPUTerrainPass;
    public override void Create()
    {
        if (m_GPUTerrainPass == null)
        {
            m_GPUTerrainPass = new GPUTerrainPass();
            m_GPUTerrainPass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
        }
    }
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (m_GPUTerrainPass != null)
        {
            renderer.EnqueuePass(m_GPUTerrainPass);
        }
    }
}

public class GPUTerrainPass : ScriptableRenderPass
{
    public GPUTerrainPass()
    {
    }
    public static Action<ScriptableRenderContext, CameraData, int > s_ExecuteAction;
    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        s_ExecuteAction?.Invoke(context, renderingData.cameraData, 1);
    }
}

