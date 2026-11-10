using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

public sealed class UIGlowTests
{
    private readonly List<GameObject> objects = new();
    private readonly List<Material> materials = new();
    private GameObject Create(string name, params System.Type[] components)
    {
        GameObject result = new(name, components);
        objects.Add(result);
        return result;
    }
    private Material CreateMaterial()
    {
        Material asset = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/UI_Glow.mat");
        Assert.That(asset, Is.Not.Null);
        Material result = new(asset);
        materials.Add(result);
        return result;
    }
    private UIGlow CreateGlow(out Graphic image, out Material material)
    {
        GameObject canvas = Create("Glow Canvas", typeof(RectTransform), typeof(Canvas));
        GameObject target = Create("Glow Image", typeof(RectTransform), typeof(RawImage));
        target.transform.SetParent(canvas.transform, false);
        image = target.GetComponent<RawImage>();
        material = CreateMaterial();
        image.material = material;
        UIGlow glow = target.AddComponent<UIGlow>();
        return glow;
    }
    private static VertexHelper Quad()
    {
        VertexHelper vertices = new();
        Color32 color = new(70, 130, 210, 128);
        vertices.AddVert(new Vector3(0, 0, 0), color, new Vector2(0.25f, 0.25f));
        vertices.AddVert(new Vector3(0, 10, 0), color, new Vector2(0.25f, 0.5f));
        vertices.AddVert(new Vector3(10, 10, 0), color, new Vector2(0.5f, 0.5f));
        vertices.AddVert(new Vector3(10, 0, 0), color, new Vector2(0.5f, 0.25f));
        vertices.AddTriangle(0, 1, 2);
        vertices.AddTriangle(2, 3, 0);
        return vertices;
    }
    private static void Tick(UIGlow glow) => typeof(UIGlow)
        .GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(glow, null);

    [TearDown]
    public void Cleanup()
    {
        foreach (GameObject item in objects) if (item != null) Object.DestroyImmediate(item);
        foreach (Material item in materials) if (item != null) Object.DestroyImmediate(item);
        objects.Clear();
        materials.Clear();
    }

    [Test]
    public void MaterialReferencesGlowShaderAndHasRequiredUIProperties()
    {
        Material material = CreateMaterial();
        Assert.That(material.shader, Is.Not.Null);
        Assert.That(material.shader.name, Is.EqualTo(UIGlow.ShaderName));
        Assert.That(ShaderUtil.ShaderHasError(material.shader), Is.False);
        foreach (string name in new[] { "_GlowRadius", "_GlowColor", "_GlowIntensity", "_Stencil",
            "_StencilComp", "_StencilOp", "_StencilReadMask", "_StencilWriteMask", "_ColorMask", "_UseUIAlphaClip" })
            Assert.That(material.HasProperty(name), Is.True, name);
        string shader = File.ReadAllText("Assets/Shaders/UIGlow.shader");
        StringAssert.Contains("UNITY_UI_CLIP_RECT", shader);
        StringAssert.Contains("_UIMaskSoftnessX", shader);
        StringAssert.Contains("Blend One OneMinusSrcAlpha", shader);
        StringAssert.Contains("color.rgb *= color.a", shader);
    }

    [Test]
    public void HaloPreservesAtlasUVsAlphaAndDrawsUnchangedSourceLast()
    {
        UIGlow glow = CreateGlow(out _, out Material material);
        using VertexHelper vertices = Quad();
        List<UIVertex> source = new();
        vertices.GetUIVertexStream(source);
        glow.ModifyMesh(vertices);
        List<UIVertex> output = new();
        vertices.GetUIVertexStream(output);
        Assert.That(output.Count, Is.EqualTo(6 * (3 * 16 + 16 + 1)));
        float weights = 0f;
        for (int i = 0; i < output.Count - source.Count; i++)
        {
            UIVertex vertex = output[i];
            UIVertex original = source[i % source.Count];
            Assert.That(vertex.uv0, Is.EqualTo(original.uv0));
            Assert.That(vertex.color, Is.EqualTo(original.color));
            Assert.That(vertex.uv1.x, Is.EqualTo(i < source.Count * 3 * 16 ? 1f : 2f));
            Assert.That(Vector3.Distance(vertex.position, original.position),
                Is.LessThanOrEqualTo(material.GetFloat("_GlowRadius") + 0.001f));
            weights += vertex.uv1.y / source.Count;
        }
        Assert.That(weights, Is.EqualTo(2f).Within(0.001f));
        for (int i = 0; i < source.Count; i++)
        {
            UIVertex original = output[output.Count - source.Count + i];
            Assert.That(original.position, Is.EqualTo(source[i].position));
            Assert.That(original.uv0, Is.EqualTo(source[i].uv0));
            Assert.That(original.color, Is.EqualTo(source[i].color));
            Assert.That(original.uv1, Is.EqualTo(Vector4.zero));
        }
    }

    [Test]
    public void DisabledZeroRadiusAndZeroIntensityLeaveSourceMeshUntouched()
    {
        UIGlow glow = CreateGlow(out _, out Material material);
        material.SetFloat("_GlowRadius", 0f);
        using (VertexHelper vertices = Quad()) { glow.ModifyMesh(vertices); Assert.That(vertices.currentVertCount, Is.EqualTo(4)); }
        material.SetFloat("_GlowRadius", 12f);
        material.SetFloat("_GlowIntensity", 0f);
        using (VertexHelper vertices = Quad()) { glow.ModifyMesh(vertices); Assert.That(vertices.currentVertCount, Is.EqualTo(4)); }
        material.SetFloat("_GlowIntensity", 3f);
        glow.enabled = false;
        using (VertexHelper vertices = Quad()) { glow.ModifyMesh(vertices); Assert.That(vertices.currentVertCount, Is.EqualTo(4)); }
    }

    [Test]
    public void EnablesUV1WithoutRemovingOtherCanvasChannelsOrReplacingMaterial()
    {
        UIGlow glow = CreateGlow(out Graphic image, out Material material);
        Canvas canvas = image.canvas;
        canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord2;
        Tick(glow);
        Assert.That((int)(canvas.additionalShaderChannels & AdditionalCanvasShaderChannels.TexCoord1), Is.Not.Zero);
        Assert.That((int)(canvas.additionalShaderChannels & AdditionalCanvasShaderChannels.TexCoord2), Is.Not.Zero);
        Assert.That(image.material, Is.SameAs(material));
        glow.enabled = false;
        Assert.That((int)(canvas.additionalShaderChannels & AdditionalCanvasShaderChannels.TexCoord1), Is.Not.Zero);
    }

    [Test]
    public void MaskMaterialReceivesLiveGlowEditsWithoutChangingStencilState()
    {
        UIGlow glow = CreateGlow(out _, out Material material);
        Material masked = StencilMaterial.Add(material, 1, StencilOp.Keep, CompareFunction.Equal, ColorWriteMask.All, 1, 0);
        try
        {
            material.SetColor("_GlowColor", Color.cyan);
            material.SetFloat("_GlowIntensity", 5f);
            Material result = glow.GetModifiedMaterial(masked);
            Assert.That(result, Is.SameAs(masked));
            Assert.That(result.GetColor("_GlowColor"), Is.EqualTo(Color.cyan));
            Assert.That(result.GetFloat("_GlowIntensity"), Is.EqualTo(5f));
            Assert.That(result.GetInt("_Stencil"), Is.EqualTo(1));
            Assert.That(result.GetInt("_StencilComp"), Is.EqualTo((int)CompareFunction.Equal));
            Assert.That(result.GetInt("_StencilWriteMask"), Is.Zero);
        }
        finally { StencilMaterial.Remove(masked); }
    }

    [Test]
    public void GlowDoesNotExpandMaskWritersOrOtherMaterialMeshes()
    {
        UIGlow glow = CreateGlow(out Graphic image, out _);
        Mask mask = image.gameObject.AddComponent<Mask>();
        using (VertexHelper vertices = Quad()) { glow.ModifyMesh(vertices); Assert.That(vertices.currentVertCount, Is.EqualTo(4)); }
        mask.enabled = false;
        image.material = null;
        using (VertexHelper vertices = Quad()) { glow.ModifyMesh(vertices); Assert.That(vertices.currentVertCount, Is.EqualTo(4)); }
    }
}
