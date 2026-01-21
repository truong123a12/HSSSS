using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteInEditMode]
[RequireComponent(typeof(Camera))]
public class ScreenSpaceScattering : MonoBehaviour
{
	private Camera mCamera;
	private CommandBuffer copyBuffer;
	private CommandBuffer blurBuffer;

	private Material prePass;
	private Material mainPass;

	// specular buffers
	private struct RGBTextures
    {
        public RenderTexture R;
        public RenderTexture G;
        public RenderTexture B;
    }

	private RGBTextures specular;

	// projection matrices
	private Matrix4x4 WorldToView;
	private Matrix4x4 ViewToWorld;
	private Matrix4x4 ViewToClip;
	private Matrix4x4 ClipToView;

	// public properties
	public Texture2D skinJitter;
	public float blurRadius;
	public float depthAware;
	public bool blurAlbedo;

	public void OnEnable()
    {
		this.mCamera = GetComponent<Camera>();

		this.prePass = new Material(Shader.Find("Hidden/HSSSS/SSSPrePass"));
		this.mainPass = new Material(Shader.Find("Hidden/HSSSS/SSSMainPass"));
		this.mainPass.SetTexture("_SkinJitter", this.skinJitter);

		this.WorldToView = Matrix4x4.identity;
		this.ViewToWorld = Matrix4x4.identity;
		this.ViewToClip = Matrix4x4.identity;
		this.ClipToView = Matrix4x4.identity;
	}

	public void OnDisable()
    {
		this.mCamera.RemoveAllCommandBuffers();
		this.RemoveSpecularRT();
    }

	public void Start ()
	{
		this.InitializeBuffers();
	}

	public void OnPreRender()
	{
		this.SetupSpecularRT();
	}

	public void OnPostRender()
	{
		this.specular.R.Release();
		this.specular.G.Release();
		this.specular.B.Release();
	}

	public void Update()
	{
		// update material properties
		this.mainPass.SetVector("_DeferredBlurredNormalsParams", new Vector2(this.blurRadius, this.depthAware * 100.0f));
		this.mainPass.SetInt("_BlurAlbedoTexture", this.blurAlbedo ? 1 : 0);

		// update projection matrices
		this.WorldToView = this.mCamera.worldToCameraMatrix;
		this.ViewToWorld = this.mCamera.worldToCameraMatrix.inverse;
		this.ViewToClip = this.mCamera.projectionMatrix;
		this.ClipToView = this.mCamera.projectionMatrix.inverse;

		Shader.SetGlobalMatrix(Shader.PropertyToID("_WorldToViewMatrix"), this.WorldToView);
		Shader.SetGlobalMatrix(Shader.PropertyToID("_ViewToWorldMatrix"), this.ViewToWorld);
		Shader.SetGlobalMatrix(Shader.PropertyToID("_ViewToClipMatrix"), this.ViewToClip);
		Shader.SetGlobalMatrix(Shader.PropertyToID("_ClipToViewMatrix"), this.ClipToView);
	}

	private void InitializeBuffers()
    {
		// screen space sss only
		Shader.EnableKeyword("_SCREENSPACE_SSS");

		int copyRT = Shader.PropertyToID("_DeferredTransmissionBuffer");
		int flipRT = Shader.PropertyToID("_TemporaryFlipRenderTexture");
		int flopRT = Shader.PropertyToID("_TemporaryFlopRenderTexture");

		//
		// transmission & ambient lights buffer
		//
		this.copyBuffer = new CommandBuffer() { name = "HSSSS.SSSPrePass" };
		this.copyBuffer.GetTemporaryRT(copyRT, -1, -1, 0, FilterMode.Point, RenderTextureFormat.R8, RenderTextureReadWrite.Linear);
		this.copyBuffer.Blit(BuiltinRenderTextureType.CameraTarget, copyRT, this.prePass, 0);
		this.mCamera.AddCommandBuffer(CameraEvent.BeforeLighting, this.copyBuffer);

		//
		// diffuse blur buffer
		//
		this.blurBuffer = new CommandBuffer() { name = "HSSSS.SSSMainPass" };
		this.blurBuffer.GetTemporaryRT(flipRT, -1, -1, 0, FilterMode.Point, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
		this.blurBuffer.GetTemporaryRT(flopRT, -1, -1, 0, FilterMode.Point, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);

		this.blurBuffer.Blit(BuiltinRenderTextureType.CurrentActive, flipRT, this.mainPass, 0);

		// separable blur
		this.blurBuffer.Blit(flipRT, flopRT, this.mainPass, 1);
		this.blurBuffer.Blit(flopRT, flipRT, this.mainPass, 2);
		this.blurBuffer.Blit(flipRT, flopRT, this.mainPass, 1);
		this.blurBuffer.Blit(flopRT, flipRT, this.mainPass, 2);

		this.blurBuffer.Blit(flipRT, flopRT, this.mainPass, 3);
		this.blurBuffer.Blit(flopRT, BuiltinRenderTextureType.CameraTarget);

		// release
		this.blurBuffer.ReleaseTemporaryRT(flipRT);
		this.blurBuffer.ReleaseTemporaryRT(flopRT);
		this.blurBuffer.ReleaseTemporaryRT(copyRT);

		this.mCamera.AddCommandBuffer(CameraEvent.AfterFinalPass, this.blurBuffer);
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
}
