using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fumo.NativePlayable
{
    internal static class NativeResultsRenderCompatibility
    {
        [Serializable] internal sealed class Observation
        {
            public bool applied,pointEnabledBeforeRender,pointEnabledAfterRender;
            public string actualRenderingPath,graphicsAPI;
        }
        internal static Observation Render(Camera camera,Light ownPoint)
        {
            bool enabled=ownPoint&&ownPoint.enabled;
            bool apply=enabled&&ownPoint.type==LightType.Point&&camera.targetTexture
                &&ownPoint.transform.root==camera.transform.root
                &&camera.actualRenderingPath==RenderingPath.DeferredShading
                &&SystemInfo.graphicsDeviceType==GraphicsDeviceType.Metal;
            var result=new Observation { applied=apply,pointEnabledBeforeRender=enabled,
                actualRenderingPath=camera.actualRenderingPath.ToString(),graphicsAPI=SystemInfo.graphicsDeviceType.ToString() };
            try
            {
                // The original Deferred portrait ignores its newly created point light during the cached Render.
                if(apply)ownPoint.enabled=false;
                camera.Render();
            }
            finally
            {
                if(ownPoint)ownPoint.enabled=enabled;
                result.pointEnabledAfterRender=ownPoint&&ownPoint.enabled;
            }
            return result;
        }
    }
}
