using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Full-screen datamosh: the image stops being refreshed and instead keeps sliding along
/// URP's motion vectors, the way a video looks when its keyframes have been deleted.
///
/// The whole effect is one persistent buffer that survives between frames. Each frame the
/// buffer is re-sampled through the motion vectors and only *some* macroblocks are allowed
/// to take fresh pixels from the camera; the rest keep smearing. <see cref="Intensity"/>
/// is the fraction of blocks that stay stale, so ramping it from 0 to 1 spreads the
/// corruption across the screen rather than switching it on.
///
/// Nothing drives it by itself — see <see cref="DatamoshOnDeath"/>, which ramps it up when
/// the player dies and clears it on respawn.
///
/// SETUP: add this feature to the renderer(s) in Assets/Settings (PC_Renderer, and
/// Mobile_Renderer if that build path is used) and assign Assets/Shaders/Datamosh.shader.
/// While Intensity is 0 the feature enqueues nothing at all, so it costs nothing when the
/// player is alive.
/// </summary>
[DisallowMultipleRendererFeature("Datamosh")]
public class DatamoshRenderFeature : ScriptableRendererFeature
{
    [Tooltip("Assets/Shaders/Datamosh.shader")]
    [SerializeField] private Shader shader;

    [Tooltip("Macroblock size in pixels. Bigger blocks read as a coarser, older codec.")]
    [Range(4f, 96f)]
    [SerializeField] private float blockSizePixels = 16f;

    [Tooltip("Multiplier on the motion vectors. Above 1 the blocks overshoot and the image " +
             "tears itself apart faster than the camera actually moves.")]
    [Range(0f, 8f)]
    [SerializeField] private float motionSmear = 1.6f;

    [Tooltip("Only mosh the camera tagged MainCamera. Off means every game camera gets it, " +
             "which is only ever right if there is exactly one.")]
    [SerializeField] private bool onlyMainCamera = true;

    [Tooltip("After post-processing by default: the project renders at MSAA 8x, and that is " +
             "the first point where the colour target is guaranteed to be a plain resolved " +
             "texture this pass can sample. Moving it before post feeds the mosh through " +
             "bloom and tonemapping, which looks softer but needs MSAA off.")]
    [SerializeField] private RenderPassEvent injectionPoint = RenderPassEvent.AfterRenderingPostProcessing;

    /// <summary>
    /// Fraction of the screen's macroblocks that refuse to take fresh pixels, 0..1.
    /// 0 disables the effect entirely (the pass is not even enqueued); 1 freezes almost
    /// everything and leaves only the motion vectors moving the image around.
    /// </summary>
    public static float Intensity
    {
        get => s_intensity;
        set => s_intensity = Mathf.Clamp01(value);
    }

    /// <summary>Extra multiplier on top of the feature's own smear setting, for a caller
    /// that wants the tearing to build independently of the block corruption.</summary>
    public static float SmearScale { get; set; } = 1f;

    /// <summary>
    /// Throws away the accumulated buffer so the next frame starts from a clean image.
    /// Call this when the view jumps somewhere unrelated — a respawn, a cutscene cut —
    /// or the mosh will happily smear the old scene over the new one.
    /// </summary>
    public static void RequestKeyframe() => s_keyframeRequested = true;

    private static float s_intensity;
    private static bool  s_keyframeRequested = true;

    private DatamoshPass _pass;
    private Material     _material;

    public override void Create()
    {
        _pass = new DatamoshPass { renderPassEvent = injectionPoint };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (_pass == null) return;

        // Alive: no buffer, no passes, no motion vector request. The next death starts from
        // a clean image because the pass releases its buffers on the way out.
        if (s_intensity <= 0.0001f)
        {
            _pass.MarkInactive();
            return;
        }

        Camera camera = renderingData.cameraData.camera;
        if (renderingData.cameraData.cameraType != CameraType.Game) return;

        // One accumulation buffer, one camera. Without this gate a second game camera —
        // the tablet minimap, a render-texture feed — would fight over the same buffer and
        // each would smear the other's frame.
        if (onlyMainCamera && (camera == null || !camera.CompareTag("MainCamera"))) return;

        if (!EnsureMaterial()) return;

        _pass.renderPassEvent = injectionPoint;
        _pass.Setup(_material, blockSizePixels, motionSmear * SmearScale, s_intensity, ref s_keyframeRequested);

        // Without this URP never renders the motion vector texture and every block would
        // freeze in place instead of sliding.
        _pass.ConfigureInput(ScriptableRenderPassInput.Motion);

        renderer.EnqueuePass(_pass);
    }

    private bool EnsureMaterial()
    {
        if (_material != null) return true;

        Shader s = shader != null ? shader : Shader.Find("Hidden/VI7/Datamosh");
        if (s == null)
        {
            Debug.LogError("[Datamosh] Shader missing. Assign Assets/Shaders/Datamosh.shader " +
                           "on the Datamosh renderer feature.");
            return false;
        }

        _material = CoreUtils.CreateEngineMaterial(s);
        return _material != null;
    }

    protected override void Dispose(bool disposing)
    {
        _pass?.Dispose();
        _pass = null;

        CoreUtils.Destroy(_material);
        _material = null;

        // Play mode ending with the player dead would otherwise leave the static intensity
        // set, and the next play session would open mid-mosh.
        s_intensity         = 0f;
        s_keyframeRequested = true;
    }

    // ─────────────────────────────────────────────────────────────────────────────────

    private class DatamoshPass : ScriptableRenderPass, System.IDisposable
    {
        private static readonly int MoshSourceId = Shader.PropertyToID("_MoshSource");
        private static readonly int MoshMotionId = Shader.PropertyToID("_MoshMotion");
        private static readonly int MoshParamsId = Shader.PropertyToID("_MoshParams");
        private static readonly int MoshBlocksId = Shader.PropertyToID("_MoshBlocks");

        private static readonly Vector4 FullScreenScaleBias = new Vector4(1f, 1f, 0f, 0f);

        private Material _material;
        private float    _blockSize;
        private float    _smear;
        private float    _intensity;
        private bool     _keyframe;

        // Ping-pong: one frame's accumulation reads from one and writes the other.
        private RTHandle _a, _b;
        private bool     _readFromA = true;
        private int      _width, _height;
        private GraphicsFormat _format;
        private bool     _warnedBackBuffer;

        private class PassData
        {
            public Material      material;
            public TextureHandle source;
            public TextureHandle motion;
            public TextureHandle previous;
            public Vector4       moshParams;
            public Vector4       moshBlocks;
            public int           shaderPass;
        }

        public void Setup(Material material, float blockSize, float smear, float intensity, ref bool keyframeRequested)
        {
            _material  = material;
            _blockSize = Mathf.Max(1f, blockSize);
            _smear     = smear;
            _intensity = intensity;

            _keyframe = keyframeRequested;
            keyframeRequested = false;
        }

        /// <summary>The effect is off this frame — drop the buffers so nothing stale is
        /// left to smear when it comes back on.</summary>
        public void MarkInactive()
        {
            ReleaseHandles();
            _keyframe = true;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_material == null) return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData   cameraData   = frameData.Get<UniversalCameraData>();

            // Reading the colour target is impossible once URP is rendering straight to the
            // backbuffer, so there is nothing to accumulate from. This is the one way the
            // effect can go quiet without anything looking broken, so it says so — a camera
            // with post-processing switched off reaches the backbuffer earlier than the
            // default injection point expects.
            if (resourceData.isActiveTargetBackBuffer)
            {
                if (!_warnedBackBuffer)
                {
                    _warnedBackBuffer = true;
                    Debug.LogWarning("[Datamosh] The camera is rendering straight to the backbuffer, " +
                                     "so there is no colour texture to mosh. Move the feature's " +
                                     "injection point earlier, or enable post-processing on the camera.");
                }
                return;
            }

            TextureHandle source = resourceData.activeColorTexture;
            if (!source.IsValid()) return;

            RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
            if (desc.width <= 0 || desc.height <= 0) return;

            bool reallocated = EnsureHandles(desc);

            TextureHandle previous = renderGraph.ImportTexture(_readFromA ? _a : _b);
            TextureHandle next     = renderGraph.ImportTexture(_readFromA ? _b : _a);

            TextureHandle motion    = resourceData.motionVectorColor;
            bool          hasMotion = motion.IsValid();

            float blocksX = Mathf.Max(1f, Mathf.Round(desc.width  / _blockSize));
            float blocksY = Mathf.Max(1f, Mathf.Round(desc.height / _blockSize));

            var moshBlocks = new Vector4(blocksX, blocksY, 1f / blocksX, 1f / blocksY);
            var moshParams = new Vector4(
                _intensity,
                hasMotion ? _smear : 0f,
                (_keyframe || reallocated) ? 1f : 0f,
                // Golden ratio so consecutive frames never rhyme, wrapped so the seed stays
                // small — the hash loses precision once it is fed six-figure inputs, and the
                // block pattern starts repeating.
                (Time.frameCount & 1023) * 0.61803398f);

            _keyframe = false;

            // Advect: previous buffer, dragged along the motion vectors, with the blocks that
            // lost their roll refreshed from the live camera image.
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Datamosh Advect", out PassData data))
            {
                data.material   = _material;
                data.source     = source;
                data.motion     = hasMotion ? motion : source;
                data.previous   = previous;
                data.moshParams = moshParams;
                data.moshBlocks = moshBlocks;
                data.shaderPass = 0;

                builder.UseTexture(data.source);
                builder.UseTexture(data.previous);
                if (hasMotion) builder.UseTexture(data.motion);

                builder.SetRenderAttachment(next, 0);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc((PassData d, RasterGraphContext ctx) => ExecuteAdvect(d, ctx));
            }

            // Composite the accumulated buffer back over the camera colour.
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Datamosh Composite", out PassData data))
            {
                data.material   = _material;
                data.previous   = next;
                data.shaderPass = 1;

                builder.UseTexture(data.previous);
                builder.SetRenderAttachment(source, 0);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc((PassData d, RasterGraphContext ctx) => ExecuteBlit(d, ctx));
            }

            _readFromA = !_readFromA;
        }

        private static void ExecuteAdvect(PassData d, RasterGraphContext ctx)
        {
            d.material.SetVector(MoshParamsId, d.moshParams);
            d.material.SetVector(MoshBlocksId, d.moshBlocks);

            ctx.cmd.SetGlobalTexture(MoshSourceId, d.source);
            ctx.cmd.SetGlobalTexture(MoshMotionId, d.motion);

            // TextureHandle converts implicitly to both RTHandle and RenderTargetIdentifier,
            // and Blitter has an overload for each — without the cast the call is ambiguous.
            Blitter.BlitTexture(ctx.cmd, (RTHandle)d.previous, FullScreenScaleBias, d.material, d.shaderPass);
        }

        private static void ExecuteBlit(PassData d, RasterGraphContext ctx)
        {
            Blitter.BlitTexture(ctx.cmd, (RTHandle)d.previous, FullScreenScaleBias, d.material, d.shaderPass);
        }

        /// <summary>Returns true when the buffers were (re)created, which means their
        /// contents are garbage and this frame has to be treated as a keyframe.</summary>
        private bool EnsureHandles(RenderTextureDescriptor desc)
        {
            GraphicsFormat format = desc.graphicsFormat;

            if (_a != null && _b != null &&
                _width == desc.width && _height == desc.height && _format == format)
                return false;

            ReleaseHandles();

            _width  = desc.width;
            _height = desc.height;
            _format = format;

            _a = RTHandles.Alloc(_width, _height, format,
                                 filterMode: FilterMode.Bilinear,
                                 wrapMode: TextureWrapMode.Clamp,
                                 name: "_DatamoshA");
            _b = RTHandles.Alloc(_width, _height, format,
                                 filterMode: FilterMode.Bilinear,
                                 wrapMode: TextureWrapMode.Clamp,
                                 name: "_DatamoshB");

            _readFromA = true;
            return true;
        }

        private void ReleaseHandles()
        {
            _a?.Release(); _a = null;
            _b?.Release(); _b = null;
            _width = _height = 0;
        }

        public void Dispose() => ReleaseHandles();
    }
}
