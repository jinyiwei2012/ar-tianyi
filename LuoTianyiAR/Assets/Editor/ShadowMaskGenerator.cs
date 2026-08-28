// ShadowMaskGenerator.cs — 裁切仓库根目录的透明角色图，生成可投影的 Alpha 轮廓。
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class ShadowMaskGenerator
{
    public const string PrimaryAssetPath = "Assets/Textures/ShadowMasks/shadow_mask_1_alpha.png";
    public const string AlternateAssetPath = "Assets/Textures/ShadowMasks/shadow_mask_2_alpha.png";

    private const int CropPaddingPixels = 8;

    [MenuItem("LuoTianyi AR/生成阴影轮廓纹理")]
    public static void GenerateAll()
    {
        Generate("shadow_mask_1.png", PrimaryAssetPath);
        Generate("shadow_mask_2.png", AlternateAssetPath);
        AssetDatabase.SaveAssets();
        Debug.Log("[ShadowMask] 两张角色轮廓纹理已生成");
    }

    public static Texture2D[] EnsureGenerated()
    {
        GenerateAll();
        return new[]
        {
            AssetDatabase.LoadAssetAtPath<Texture2D>(PrimaryAssetPath),
            AssetDatabase.LoadAssetAtPath<Texture2D>(AlternateAssetPath)
        };
    }

    private static void Generate(string sourceFileName, string assetPath)
    {
        string sourcePath = Path.GetFullPath(
            Path.Combine(Application.dataPath, "..", "..", sourceFileName));
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException($"[ShadowMask] 找不到源图: {sourcePath}");

        byte[] sourceBytes = File.ReadAllBytes(sourcePath);
        var source = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
        if (!source.LoadImage(sourceBytes, false))
        {
            UnityEngine.Object.DestroyImmediate(source);
            throw new InvalidOperationException($"[ShadowMask] 无法读取源图: {sourcePath}");
        }

        try
        {
            Color32[] pixels = source.GetPixels32();
            int width = source.width;
            int height = source.height;
            bool hasUsefulAlpha = Array.Exists(pixels, pixel => pixel.a < 250);
            if (!hasUsefulAlpha)
                throw new InvalidOperationException(
                    $"[ShadowMask] 源图没有有效 Alpha，请导出透明 PNG: {sourcePath}");
            bool[] background = MarkTransparentPixels(pixels);

            if (!TryGetForegroundBounds(
                    background,
                    width,
                    height,
                    out int minX,
                    out int minY,
                    out int maxX,
                    out int maxY))
                throw new InvalidOperationException($"[ShadowMask] 源图没有可用轮廓: {sourcePath}");

            minX = Mathf.Max(0, minX - CropPaddingPixels);
            minY = Mathf.Max(0, minY - CropPaddingPixels);
            maxX = Mathf.Min(width - 1, maxX + CropPaddingPixels);
            maxY = Mathf.Min(height - 1, maxY + CropPaddingPixels);
            int outputWidth = maxX - minX + 1;
            int outputHeight = maxY - minY + 1;
            var outputPixels = new Color32[outputWidth * outputHeight];

            for (int y = 0; y < outputHeight; y++)
            {
                int sourceY = minY + y;
                for (int x = 0; x < outputWidth; x++)
                {
                    int sourceX = minX + x;
                    int sourceIndex = sourceY * width + sourceX;
                    byte alpha = background[sourceIndex] ? (byte)0 : pixels[sourceIndex].a;
                    outputPixels[y * outputWidth + x] = new Color32(255, 255, 255, alpha);
                }
            }

            var output = new Texture2D(
                outputWidth,
                outputHeight,
                TextureFormat.RGBA32,
                false,
                true);
            try
            {
                output.SetPixels32(outputPixels);
                output.Apply(false, false);
                string absoluteAssetPath = Path.GetFullPath(
                    Path.Combine(Application.dataPath, "..", assetPath));
                Directory.CreateDirectory(Path.GetDirectoryName(absoluteAssetPath));
                File.WriteAllBytes(absoluteAssetPath, output.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(output);
            }

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            if (AssetImporter.GetAtPath(assetPath) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Default;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = true;
                importer.sRGBTexture = false;
                importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            Debug.Log(
                $"[ShadowMask] {sourceFileName} -> {assetPath}, " +
                $"source={width}x{height}, crop={outputWidth}x{outputHeight}, " +
                "sourceAlpha=present");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(source);
        }
    }

    private static bool[] MarkTransparentPixels(Color32[] pixels)
    {
        var background = new bool[pixels.Length];
        for (int i = 0; i < pixels.Length; i++)
            background[i] = pixels[i].a == 0;
        return background;
    }

    private static bool TryGetForegroundBounds(
        bool[] background,
        int width,
        int height,
        out int minX,
        out int minY,
        out int maxX,
        out int maxY)
    {
        minX = width;
        minY = height;
        maxX = -1;
        maxY = -1;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (background[y * width + x])
                    continue;
                minX = Mathf.Min(minX, x);
                minY = Mathf.Min(minY, y);
                maxX = Mathf.Max(maxX, x);
                maxY = Mathf.Max(maxY, y);
            }
        }
        return maxX >= minX && maxY >= minY;
    }
}
