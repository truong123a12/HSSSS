using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace HSSSS
{
    //[DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public class DeferredRenderer : MonoBehaviour
    {
        private Camera mCamera;

        private CommandBuffer copyBuffer;
        private CommandBuffer blurBuffer;

        private const string CopyBufferName = "HSSSS.SSSPrePass";
        private const string BlurBufferName = "HSSSS.SSSMainPass";

        private static Material prePass;
        private static Material mainPass;

        private static int count;
        private static readonly int frameCount = Shader.PropertyToID("_FrameCount");

        public enum LUTProfile
        {
            none,
            nvidia1,
            nvidia2
            //jimenez
        }


        public LUTProfile lutProfile = LUTProfile.none;

        public float skinLutBias = 0.0f;
        public float skinLutScale = 0.5f;

        public float shadowLutBias = 0.0f;
        public float shadowLutScale = 1.0f;

        public bool screenSpaceSSS = true;
        public float sssBlurWeight = 1.0f;
        public float sssBlurRadius = 50f;
        public float sssBlurDepthRange = 1.0f;

        public int sssBlurIter = 3;

        public bool sssBlurAlbedo = true;

        public Vector3 colorBleedWeights = new Vector3(0.40f, 0.15f, 0.20f);
        public Vector3 transAbsorption = new Vector3(-8.00f, -48.0f, -64.0f);

        public bool bakedThickness = true;

        public float transWeight = 1.0f;
        public float transShadowWeight = 0.5f;
        public float transDistortion = 0.5f;
        public float transFalloff = 2.0f;
        public float thicknessBias = 0.5f;

        public bool microDetails = false;

        //microDetailWeight_1 = new KeyValue<int, float>(Shader.PropertyToID("_DetailNormalMapScale_2"), 0.5f),
        //microDetailWeight_2 = new KeyValue<int, float>(Shader.PropertyToID("_DetailNormalMapScale_3"), 0.5f),
        //microDetailOcclusion = new KeyValue<int, float>(Shader.PropertyToID("_PoreOcclusionStrength"), 0.5f),

        public float microDetailTiling = 64.0f;

        public Texture2D skinJitter,
            pennerDiffuse,
            nvidiaDiffuse,
            nvidiaShadow,
            deepScatter;

        private struct RGBTextures
        {
            public RenderTexture R;
            public RenderTexture G;
            public RenderTexture B;
        }

        private int cachedBlurIter = -1; // Добавьте это поле
        private RGBTextures specular;

        public void Awake()
        {
            prePass = new Material(Shader.Find("Hidden/HSSSS/SSSPrePass"));
            mainPass = new Material(Shader.Find("Hidden/HSSSS/SSSMainPass"));

            count = 1;
        }

        public void OnEnable()
        {
            this.mCamera = GetComponent<Camera>();
            
            this.SetupCommandBuffers();
        }

        private void Update()
        {
            this.RefreshProperties();

            // Проверяем изменение количества итераций blur
            if (cachedBlurIter != sssBlurIter)
            {
                cachedBlurIter = sssBlurIter;

                // Пересоздаём Command Buffer'ы
                this.RemoveCommandBuffers();
                this.SetupCommandBuffers();

                // Обновляем спекуляр буферы если нужно
                if (screenSpaceSSS)
                {
                    this.RemoveSpecularRT();
                }
            }
        }

        public void OnDisable()
        {
            this.RemoveCommandBuffers();
        }

        private void OnPreRender()
        {
            Shader.SetGlobalInt(frameCount, count);
            count = (count + 1) % 64;

            if (screenSpaceSSS)
            {
                this.SetupSpecularRT();
            }
        }

        private void OnPostRender()
        {
            if (screenSpaceSSS)
            {
                this.specular.R.Release();
                this.specular.G.Release();
                this.specular.B.Release();
            }
        }

        private void SetupSpecularRT()
        {
            int width = this.mCamera.pixelWidth;
            int height = this.mCamera.pixelHeight;

            if (this.mCamera.targetTexture)
            {
                width = this.mCamera.targetTexture.width;
                height = this.mCamera.targetTexture.height;
            }

            this.specular.R = new RenderTexture(width, height, 0, RenderTextureFormat.RHalf, RenderTextureReadWrite.Linear)
            {
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                autoGenerateMips = false
            };

            this.specular.G = new RenderTexture(width, height, 0, RenderTextureFormat.RHalf, RenderTextureReadWrite.Linear)
            {
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                autoGenerateMips = false
            };

            this.specular.B = new RenderTexture(width, height, 0, RenderTextureFormat.RHalf, RenderTextureReadWrite.Linear)
            {
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                autoGenerateMips = false
            };

            this.specular.R.SetGlobalShaderProperty("_SpecularBufferR");
            this.specular.G.SetGlobalShaderProperty("_SpecularBufferG");
            this.specular.B.SetGlobalShaderProperty("_SpecularBufferB");

            this.specular.R.Create();
            this.specular.G.Create();
            this.specular.B.Create();

            RenderTexture rt = RenderTexture.active;
            RenderTexture.active = this.specular.R;
            GL.Clear(true, true, Color.black);
            RenderTexture.active = this.specular.G;
            GL.Clear(true, true, Color.black);
            RenderTexture.active = this.specular.B;
            GL.Clear(true, true, Color.black);
            RenderTexture.active = rt;

            Graphics.ClearRandomWriteTargets();
            Graphics.SetRandomWriteTarget(1, this.specular.R);
            Graphics.SetRandomWriteTarget(2, this.specular.G);
            Graphics.SetRandomWriteTarget(3, this.specular.B);
        }

        private void RemoveSpecularRT()
        {
            if (this.specular.R)
            {
                this.specular.R.Release();
                DestroyImmediate(this.specular.R);
                this.specular.R = null;
            }

            if (this.specular.G)
            {
                this.specular.G.Release();
                DestroyImmediate(this.specular.G);
                this.specular.G = null;
            }

            if (this.specular.B)
            {
                this.specular.B.Release();
                DestroyImmediate(this.specular.B);
                this.specular.B = null;
            }
        }

        #region Properties Control
        private void RefreshSkinProperties()
        {
            Shader.SetGlobalVector("_DeferredSkinParams",
                new Vector4(
                    1.0f,
                    skinLutBias,
                    skinLutScale,
                    sssBlurWeight
                    ));
            Shader.SetGlobalVector("_DeferredShadowParams",
                new Vector2(
                    shadowLutBias,
                    shadowLutScale
                    ));
            Shader.SetGlobalVector("_DeferredSkinColorBleedAoWeights", colorBleedWeights);
            Shader.SetGlobalVector("_DeferredSkinTransmissionAbsorption", transAbsorption);
        }

        private void RefreshBlurProperties()
        {
            //mainPass.SetTexture("_SkinJitter", skinJitter);
            mainPass.SetVector("_DeferredBlurredNormalsParams",
                new Vector2(
                    sssBlurRadius,
                    sssBlurDepthRange * 100.0f
                    ));
            mainPass.SetInt("_BlurAlbedoTexture", sssBlurAlbedo ? 1 : 0);
        }

        private void RefreshLookupProperties()
        {
            Shader.DisableKeyword("_FACEWORKS_TYPE1");
            Shader.DisableKeyword("_FACEWORKS_TYPE2");
            Shader.DisableKeyword("_SCREENSPACE_SSS");

            // lookup texture replacement
            switch (lutProfile)
            {
                case LUTProfile.none:

                    Shader.SetGlobalTexture("_DeferredSkinLut", null);
                    Shader.SetGlobalTexture("_DeferredShadowLut", null);
                    Shader.DisableKeyword("_FACEWORKS_TYPE1");
                    Shader.DisableKeyword("_FACEWORKS_TYPE2");
                    break;

                case LUTProfile.nvidia1:
                    Shader.EnableKeyword("_FACEWORKS_TYPE1");
                    Shader.SetGlobalTexture("_DeferredSkinLut", nvidiaDiffuse);
                    break;

                case LUTProfile.nvidia2:
                    Shader.EnableKeyword("_FACEWORKS_TYPE2");
                    Shader.SetGlobalTexture("_DeferredSkinLut", nvidiaDiffuse);
                    Shader.SetGlobalTexture("_DeferredShadowLut", nvidiaShadow);
                    break;

                //case LUTProfile.jimenez:
                //    Shader.EnableKeyword("_SCREENSPACE_SSS");
                //    break;

            }
            if (screenSpaceSSS)
            {
                Shader.EnableKeyword("_SCREENSPACE_SSS");
            }
        }

        private void RefreshTransmissionProperties()
        {
            Shader.DisableKeyword("_BAKED_THICKNESS");

            if (bakedThickness)
            {
                Shader.EnableKeyword("_BAKED_THICKNESS");
            }

            else
            {
                Shader.SetGlobalTexture("_DeferredTransmissionLut", deepScatter);
                Shader.SetGlobalFloat("_DeferredThicknessBias", thicknessBias * 0.01f);
            }

            Shader.SetGlobalVector("_DeferredTransmissionParams",
                new Vector4(
                    transWeight,
                    transFalloff,
                    transDistortion,
                    transShadowWeight
                    ));
        }

        private void RefreshProperties()
        {
            this.RefreshSkinProperties();
            this.RefreshBlurProperties();
            this.RefreshLookupProperties();
            this.RefreshTransmissionProperties();
        }
        #endregion

        #region Commandbuffer Control

        private void SetupDiffuseBlurBuffer()
        {
            int copyRT = Shader.PropertyToID("_DeferredTransmissionBuffer");
            int flipRT = Shader.PropertyToID("_TemporaryFlipRenderTexture");
            int flopRT = Shader.PropertyToID("_TemporaryFlopRenderTexture");

            ///////////////////////////////////
            ///////////////////////////////////
            // transmission & ambient lights //
            ///////////////////////////////////
            ///////////////////////////////////

            this.copyBuffer = new CommandBuffer() { name = CopyBufferName };

            // get temporary render textures
            this.copyBuffer.GetTemporaryRT(copyRT, -1, -1, 0, FilterMode.Point, RenderTextureFormat.R8, RenderTextureReadWrite.Linear);
            // extract thickness map from g-buffer 3
            this.copyBuffer.Blit(BuiltinRenderTextureType.CameraTarget, copyRT, prePass, 0);
            // add command buffer
            this.mCamera.AddCommandBuffer(CameraEvent.BeforeLighting, this.copyBuffer);

            ///////////////////////////////////
            ///////////////////////////////////
            //// screen space diffuse blur ////
            ///////////////////////////////////
            ///////////////////////////////////

            this.blurBuffer = new CommandBuffer() { name = BlurBufferName };

            // get temporary render textures
            this.blurBuffer.GetTemporaryRT(flipRT, -1, -1, 0, FilterMode.Point, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            this.blurBuffer.GetTemporaryRT(flopRT, -1, -1, 0, FilterMode.Point, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);

            this.blurBuffer.Blit(BuiltinRenderTextureType.CurrentActive, flipRT, mainPass, 0);

            // separable blur
            for (int i = 0; i < sssBlurIter; i++)
            {
                this.blurBuffer.Blit(flipRT, flopRT, mainPass, 1);
                this.blurBuffer.Blit(flopRT, flipRT, mainPass, 2);
            }

            // collect all lighting
            this.blurBuffer.Blit(flipRT, flopRT, mainPass, 3);

            // to camera target
            this.blurBuffer.Blit(flopRT, BuiltinRenderTextureType.CameraTarget);

            // release render textures
            this.blurBuffer.ReleaseTemporaryRT(flipRT);
            this.blurBuffer.ReleaseTemporaryRT(flopRT);
            this.blurBuffer.ReleaseTemporaryRT(copyRT);

            // add command buffer
            this.mCamera.AddCommandBuffer(CameraEvent.AfterFinalPass, this.blurBuffer);
        }

        private void SetupNormalBlurBuffer()
        {
            ///////////////////////////////////////
            ///////////////////////////////////////
            //// transmission & ambient lights ////
            ///////////////////////////////////////
            ///////////////////////////////////////

            int copyRT = Shader.PropertyToID("_DeferredTransmissionBuffer");

            this.copyBuffer = new CommandBuffer() { name = CopyBufferName };
            // get temporary render textures
            this.copyBuffer.GetTemporaryRT(copyRT, -1, -1, 0, FilterMode.Point, RenderTextureFormat.RHalf, RenderTextureReadWrite.Linear);
            // extract thickness map from g-buffer 3
            this.copyBuffer.Blit(BuiltinRenderTextureType.CameraTarget, copyRT, prePass, 0);
            // add command buffer
            this.mCamera.AddCommandBuffer(CameraEvent.BeforeLighting, this.copyBuffer);

            //////////////////////////////////
            //////////////////////////////////
            //// screen space normal blur ////
            //////////////////////////////////
            //////////////////////////////////

            this.blurBuffer = new CommandBuffer() { name = BlurBufferName };

            if (sssBlurIter > 0)
            {
                int flipRT = Shader.PropertyToID("_TemporaryFlipRenderTexture");
                int flopRT = Shader.PropertyToID("_TemporaryFlopRenderTexture");

                this.blurBuffer.GetTemporaryRT(flipRT, -1, -1, 0, FilterMode.Point, RenderTextureFormat.ARGB2101010, RenderTextureReadWrite.Linear);
                this.blurBuffer.GetTemporaryRT(flopRT, -1, -1, 0, FilterMode.Point, RenderTextureFormat.ARGB2101010, RenderTextureReadWrite.Linear);

                this.blurBuffer.Blit(BuiltinRenderTextureType.GBuffer2, flipRT, mainPass, 4);
                this.blurBuffer.Blit(flipRT, flopRT, mainPass, 5);

                for (int i = 1; i < sssBlurIter; i++)
                {
                    this.blurBuffer.Blit(flopRT, flipRT, mainPass, 4);
                    this.blurBuffer.Blit(flipRT, flopRT, mainPass, 5);
                }

                this.blurBuffer.SetGlobalTexture("_DeferredBlurredNormalBuffer", flopRT);

                this.blurBuffer.ReleaseTemporaryRT(flipRT);
                this.blurBuffer.ReleaseTemporaryRT(flopRT);
            }

            else
            {
                this.blurBuffer.SetGlobalTexture("_DeferredBlurredNormalBuffer", BuiltinRenderTextureType.GBuffer2);
            }

            this.mCamera.AddCommandBuffer(CameraEvent.BeforeLighting, this.blurBuffer);
        }

        private void SetupDummyBuffer()
        {
            int copyRT = Shader.PropertyToID("_DeferredTransmissionBuffer");

            ///////////////////////////////////
            // transmission & ambient lights //
            ///////////////////////////////////
            this.copyBuffer = new CommandBuffer() { name = CopyBufferName };
            // get temporary rendertextures
            this.copyBuffer.GetTemporaryRT(copyRT, -1, -1, 0, FilterMode.Point, RenderTextureFormat.R8, RenderTextureReadWrite.Linear);
            // extract thickness map from gbuffer 3
            this.copyBuffer.Blit(BuiltinRenderTextureType.CameraTarget, copyRT, prePass, 0);
            // release rendertexture
            this.copyBuffer.ReleaseTemporaryRT(copyRT);
            // add commandbuffer
            this.mCamera.AddCommandBuffer(CameraEvent.AfterGBuffer, this.copyBuffer);
        }

        private void SetupCommandBuffers()
        {
            //buffer 0: hsr compatible buffer
            //if (HSSSS.hsrCompatible)
            //{
            //SetupDummyBuffer();
            //}

            //else
            //{
                // buffer 1: screen space scattering
                if (screenSpaceSSS)
                {
                    this.SetupDiffuseBlurBuffer();
                }

                // buffer 2: pre-integrated scattering
                //else
                //{
                    this.SetupNormalBlurBuffer();
            //    }
            //}
        }

        private void RemoveCommandBuffers()
        {
            foreach (CommandBuffer buffer in this.mCamera.GetCommandBuffers(CameraEvent.BeforeLighting))
            {
                if (buffer.name == CopyBufferName || buffer.name == BlurBufferName)
                {
                    this.mCamera.RemoveCommandBuffer(CameraEvent.BeforeLighting, buffer);
                }
            }

            foreach (CommandBuffer buffer in this.mCamera.GetCommandBuffers(CameraEvent.AfterFinalPass))
            {
                if (buffer.name == BlurBufferName)
                {
                    this.mCamera.RemoveCommandBuffer(CameraEvent.AfterFinalPass, buffer);
                }
            }

            this.copyBuffer = null;
            this.blurBuffer = null;
        }
        #endregion

        #region Interfaces
        public void UpdateSkinSettings()
        {
            this.RefreshSkinProperties();
            this.RefreshBlurProperties();
            this.RefreshLookupProperties();
            this.RefreshTransmissionProperties();

            this.RemoveCommandBuffers();
            this.SetupCommandBuffers();

            if (screenSpaceSSS)
            {
                this.RemoveSpecularRT();
            }
        }
        #endregion
    }

}