using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>Native Graphic pipeline tests, plus isolated built-in-camera GPU readbacks.</summary>
public sealed class UIGlowRenderTests
{
    private readonly List<Object> created = new();
    private GameObject Create(string name, params System.Type[] types)
    {
        GameObject result = new(name, types);
        result.layer = 31;
        created.Add(result);
        return result;
    }
    private static void Set(object target, string field, object value) => target.GetType()
        .GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
    private static void Tick(object target) => target.GetType()
        .GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, null);
    private static void Rebuild(Graphic graphic)
    {
        graphic.SetAllDirty();
        graphic.canvasRenderer.cull = false;
        graphic.Rebuild(CanvasUpdate.PreRender);
    }
    private Material Material()
    {
        Material asset = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/UI_Glow.mat");
        Assert.That(asset, Is.Not.Null);
        Material material = new(asset);
        created.Add(material);
        return material;
    }
    private Graphic Graphic(Transform parent, bool polygon, Material material)
    {
        GameObject target = Create("Source", typeof(RectTransform));
        target.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)target.transform;
        rect.anchorMin = rect.anchorMax = Vector2.one * 0.5f;
        rect.sizeDelta = Vector2.one * 64f;
        Graphic graphic;
        if (polygon)
        {
            UICustomPolygonRenderer renderer = target.AddComponent<UICustomPolygonRenderer>();
            List<RectTransform> points = new();
            foreach (Vector2 position in new[] { new Vector2(-32, -32), new Vector2(-32, 32), new Vector2(32, 32), new Vector2(32, -32) })
            {
                RectTransform point = (RectTransform)Create("Vertex", typeof(RectTransform)).transform;
                point.SetParent(rect, false);
                point.anchorMin = point.anchorMax = Vector2.one * 0.5f;
                point.anchoredPosition = position;
                points.Add(point);
            }
            Set(renderer, "vertices", points);
            graphic = renderer;
        }
        else
        {
            graphic = target.AddComponent<Image>();
            target.AddComponent<UIGlow>();
        }
        graphic.color = Color.black;
        graphic.raycastTarget = false;
        graphic.material = material;
        if (polygon) Tick(graphic);
        else Tick(graphic.GetComponent<UIGlow>());
        return graphic;
    }

    [TearDown]
    public void Cleanup()
    {
        foreach (Object item in created) if (item != null) Object.DestroyImmediate(item);
        created.Clear();
    }

    [Test]
    public void PolygonMaterialAloneRunsGlowThroughTheNativeCanvasRenderer()
    {
        Canvas canvas = Create("Canvas", typeof(RectTransform), typeof(Canvas)).GetComponent<Canvas>();
        Graphic polygon = Graphic(canvas.transform, true, Material());
        Rebuild(polygon);
        Mesh mesh = polygon.canvasRenderer.GetMesh();
        Assert.That(mesh.vertexCount, Is.EqualTo(4));
        Assert.That(polygon.materialForRendering.GetInteger("_GlowPolygonCount"), Is.EqualTo(4));
        Assert.That((int)(canvas.additionalShaderChannels & AdditionalCanvasShaderChannels.TexCoord1), Is.Not.Zero);
        UIGlow explicitEffect = polygon.gameObject.AddComponent<UIGlow>();
        Rebuild(polygon);
        mesh = polygon.canvasRenderer.GetMesh();
        Assert.That(mesh.vertexCount, Is.EqualTo(4), "An explicit effect must not double the polygon's automatic halo.");
        Assert.That(polygon.materialForRendering.GetInteger("_GlowPolygonCount"), Is.EqualTo(4));
        explicitEffect.enabled = false;
        Rebuild(polygon);
        mesh = polygon.canvasRenderer.GetMesh();
        Assert.That(mesh.vertexCount, Is.EqualTo(4));
        Assert.That(polygon.materialForRendering.GetInteger("_GlowPolygonCount"), Is.Zero);
    }

    private Camera Camera(out Canvas canvas, out RenderTexture target)
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("GPU readback needs a graphics device.");
        if (GraphicsSettings.currentRenderPipeline != null) Assert.Ignore("Run the built-in-camera GPU fixture in the isolated Glow verification project.");
        Camera camera = Create("Readback Camera", typeof(Camera)).GetComponent<Camera>();
        camera.enabled = false;
        camera.transform.position = new Vector3(0, 0, -10);
        camera.orthographic = true;
        camera.orthographicSize = 128;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 1);
        camera.cullingMask = 1 << 31;
        target = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGBFloat);
        created.Add(target);
        target.Create();
        camera.targetTexture = target;
        canvas = Create("Readback Canvas", typeof(RectTransform), typeof(Canvas)).GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = 1f;
        return camera;
    }
    private Texture2D Capture(Camera camera, Graphic source, RenderTexture target)
    {
        if (source is UICustomPolygonRenderer) Tick(source);
        else Tick(source.GetComponent<UIGlow>());
        Rebuild(source);
        Canvas.ForceUpdateCanvases();
        camera.Render();
        RenderTexture previous = RenderTexture.active;
        try
        {
            RenderTexture.active = target;
            Texture2D pixels = new(256, 256, TextureFormat.RGBAFloat, false, true);
            created.Add(pixels);
            pixels.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
            pixels.Apply();
            return pixels;
        }
        finally { RenderTexture.active = previous; }
    }

    private void SaveCapture(Texture2D pixels, string fileName)
    {
        string outputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "../VerificationResults"));
        Directory.CreateDirectory(outputDirectory);
        Texture2D screenshot = new(pixels.width, pixels.height, TextureFormat.RGBA32, false);
        created.Add(screenshot);
        Color[] colors = pixels.GetPixels();
        if (QualitySettings.activeColorSpace == ColorSpace.Linear)
            for (int i = 0; i < colors.Length; i++) colors[i] = colors[i].gamma;
        screenshot.SetPixels(colors);
        screenshot.Apply();
        File.WriteAllBytes(Path.Combine(outputDirectory, fileName), screenshot.EncodeToPNG());
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DefaultGlowHasClearlyVisiblePixelsOutsideImageAndPolygon(bool polygon)
    {
        Camera camera = Camera(out Canvas canvas, out RenderTexture target);
        Material material = Material();
        Graphic graphic = Graphic(canvas.transform, polygon, material);
        material.SetFloat("_GlowIntensity", 0);
        Color baseline = Capture(camera, graphic, target).GetPixel(164, 128);
        material.SetFloat("_GlowIntensity", 3);
        Texture2D glowing = Capture(camera, graphic, target);
        Color halo = glowing.GetPixel(164, 128);
        TestContext.Out.WriteLine($"{(polygon ? "Polygon" : "Image")}: outside background={baseline}, halo={halo}, center={glowing.GetPixel(128, 128)}");
        Assert.That(halo.r - baseline.r, Is.GreaterThan(0.25f), $"Outside halo: baseline {baseline}, glow {halo}");
        Assert.That(glowing.GetPixel(128, 128).r, Is.LessThan(0.05f), "The original black source must remain black.");
        SaveCapture(glowing, polygon ? "UIGlow-Polygon.png" : "UIGlow-Image.png");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MaskedPolygonKeepsHaloInsideItsMask(bool stencil)
    {
        Camera camera = Camera(out Canvas canvas, out RenderTexture target);
        GameObject viewport = Create("Mask", typeof(RectTransform));
        viewport.transform.SetParent(canvas.transform, false);
        ((RectTransform)viewport.transform).sizeDelta = Vector2.one * 84;
        if (stencil)
        {
            viewport.AddComponent<Image>();
            viewport.AddComponent<Mask>().showMaskGraphic = false;
        }
        else viewport.AddComponent<RectMask2D>();
        Graphic polygon = Graphic(viewport.transform, true, Material());
        Texture2D image = Capture(camera, polygon, target);
        Assert.That(image.GetPixel(164, 128).r, Is.GreaterThan(0.4f));
        Color outside = image.GetPixel(174, 128);
        Color background = image.GetPixel(10, 10);
        Assert.That(outside.r, Is.EqualTo(background.r).Within(0.02f));
        Assert.That(outside.g, Is.EqualTo(background.g).Within(0.02f));
        SaveCapture(image, stencil ? "UIGlow-StencilMask.png" : "UIGlow-RectMask.png");
    }

    [Test]
    public void TriangleGlowFollowsAnimatedVertexWithoutNeedingAnEffectComponent()
    {
        Camera camera = Camera(out Canvas canvas, out RenderTexture target);
        Graphic graphic = Graphic(canvas.transform, true, Material());
        UICustomPolygonRenderer polygon = (UICustomPolygonRenderer)graphic;
        List<RectTransform> points = new(polygon.Vertices);
        points[0].anchoredPosition = new Vector2(-32, -32);
        points[1].anchoredPosition = new Vector2(-32, 32);
        points[2].anchoredPosition = new Vector2(32, 0);
        points.RemoveAt(3);
        Set(polygon, "vertices", points);
        polygon.RefreshGeometry();
        Texture2D first = Capture(camera, polygon, target);
        Assert.That(first.GetPixel(91, 128).r, Is.GreaterThan(0.45f), "The vertical edge must emit visible glow.");
        Assert.That(first.GetPixel(164, 128).r, Is.GreaterThan(0.45f), "The triangle tip must also emit glow.");
        SaveCapture(first, "UIGlow-Triangle.png");
        points[2].anchoredPosition = new Vector2(52, 0);
        Texture2D changed = Capture(camera, polygon, target);
        Assert.That(changed.GetPixel(164, 128).r, Is.LessThan(0.05f), "The previous tip position is now inside the black source.");
        Assert.That(changed.GetPixel(184, 128).r, Is.GreaterThan(0.45f), "Glow must follow the moved tip.");
        SaveCapture(changed, "UIGlow-TriangleMoved.png");
        TestContext.Out.WriteLine($"Triangle: edge={first.GetPixel(91, 128)}, tip={first.GetPixel(164, 128)}, moved tip={changed.GetPixel(184, 128)}");
    }

    [Test]
    public void ContinuousPolygonFalloffHasNoRingStepsAndEndsAtTheBackground()
    {
        Camera camera = Camera(out Canvas canvas, out RenderTexture target);
        Graphic polygon = Graphic(canvas.transform, true, Material());
        Texture2D image = Capture(camera, polygon, target);
        float largestStep = 0f;
        float previous = image.GetPixel(162, 128).r;
        List<string> values = new();
        for (int x = 163; x <= 174; x++)
        {
            float next = image.GetPixel(x, 128).r;
            Assert.That(next, Is.LessThanOrEqualTo(previous + 0.002f), $"Non-monotonic fade at {x}");
            largestStep = Mathf.Max(largestStep, previous - next);
            previous = next;
            values.Add(next.ToString("F4"));
        }
        Assert.That(largestStep, Is.LessThan(0.18f));
        Assert.That(image.GetPixel(171, 128).r - image.GetPixel(174, 128).r, Is.LessThan(0.005f));
        TestContext.Out.WriteLine("Continuous contour samples: " + string.Join(", ", values));
    }

    [Test]
    public void SDRGlowMatchesHDRAndLDRRenderTargets()
    {
        Camera camera = Camera(out Canvas canvas, out RenderTexture hdr);
        Material material = Material();
        Graphic polygon = Graphic(canvas.transform, true, material);
        Texture2D first = Capture(camera, polygon, hdr);
        RenderTexture ldr = new(256, 256, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        created.Add(ldr); ldr.Create(); camera.targetTexture = ldr;
        Texture2D second = Capture(camera, polygon, ldr);
        foreach (int x in new[] { 128, 160, 162, 164, 166, 169, 172 })
        {
            Color a = first.GetPixel(x, 128), b = second.GetPixel(x, 128);
            Assert.That(Mathf.Abs(a.r - b.r), Is.LessThan(0.012f));
            Assert.That(Mathf.Abs(a.g - b.g), Is.LessThan(0.012f));
            Assert.That(Mathf.Abs(a.b - b.b), Is.LessThan(0.012f));
        }
        SaveCapture(first, "UIGlow-HDRTarget.png");
        SaveCapture(second, "UIGlow-LDRTarget.png");
    }

    [Test]
    public void SceneAndGameCameraTypesUseMatchingUnprocessedGlow()
    {
        Camera camera = Camera(out Canvas canvas, out RenderTexture target);
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.transform.position = Vector3.zero;
        canvas.transform.localScale = Vector3.one;
        ((RectTransform)canvas.transform).sizeDelta = Vector2.one * 256;
        Graphic polygon = Graphic(canvas.transform, true, Material());
        polygon.color = Color.white;
        camera.cameraType = CameraType.Game;
        Texture2D game = Capture(camera, polygon, target);
        camera.cameraType = CameraType.SceneView;
        Texture2D scene = Capture(camera, polygon, target);
        foreach (int x in new[] { 128, 160, 162, 164, 166, 169, 172 })
        {
            Color a = game.GetPixel(x, 128), b = scene.GetPixel(x, 128);
            Assert.That(Mathf.Abs(a.r - b.r), Is.LessThan(0.005f));
            Assert.That(Mathf.Abs(a.g - b.g), Is.LessThan(0.005f));
            Assert.That(Mathf.Abs(a.b - b.b), Is.LessThan(0.005f));
        }
        SaveCapture(game, "UIGlow-GameCamera.png");
        SaveCapture(scene, "UIGlow-SceneCamera.png");
    }

    [Test]
    public void TranslatedRotatedScaledPolygonKeepsGlowInGraphicLocalSpace()
    {
        Camera camera = Camera(out Canvas canvas, out RenderTexture target);
        Graphic polygon = Graphic(canvas.transform, true, Material());
        polygon.rectTransform.anchoredPosition = new Vector2(30, -10);
        polygon.transform.localRotation = Quaternion.Euler(0, 0, 30);
        polygon.transform.localScale = new Vector3(0.8f, 1.2f, 1);
        Texture2D image = Capture(camera, polygon, target);
        Vector2 center = RectTransformUtility.WorldToScreenPoint(camera, polygon.transform.TransformPoint(Vector3.zero));
        Vector2 halo = RectTransformUtility.WorldToScreenPoint(camera, polygon.transform.TransformPoint(new Vector3(36.5f, 0)));
        Assert.That(image.GetPixel(Mathf.RoundToInt(center.x), Mathf.RoundToInt(center.y)).r, Is.LessThan(0.05f));
        Assert.That(image.GetPixel(Mathf.RoundToInt(halo.x), Mathf.RoundToInt(halo.y)).r, Is.GreaterThan(0.35f));
        SaveCapture(image, "UIGlow-Transformed.png");
    }

    [Test]
    public void FirstValidShapeAfterCollapsedAnimationBindsItsAnalyticMaterial()
    {
        Canvas canvas = Create("Collapse Canvas", typeof(RectTransform), typeof(Canvas)).GetComponent<Canvas>();
        UICustomPolygonRenderer polygon = (UICustomPolygonRenderer)Graphic(canvas.transform, true, Material());
        foreach (RectTransform vertex in polygon.Vertices) vertex.anchoredPosition = Vector2.zero;
        Rebuild(polygon);
        Assert.That(polygon.canvasRenderer.GetMaterial().GetInteger("_GlowPolygonCount"), Is.Zero);
        Vector2[] positions = { new(-32, -32), new(-32, 32), new(32, 32), new(32, -32) };
        for (int i = 0; i < positions.Length; i++) polygon.Vertices[i].anchoredPosition = positions[i];
        Tick(polygon);
        // Do not SetAllDirty here: animation/vertex tracking must queue the material update itself.
        polygon.Rebuild(CanvasUpdate.PreRender);
        Assert.That(polygon.canvasRenderer.GetMaterial().GetInteger("_GlowPolygonCount"), Is.EqualTo(4));
    }
}
