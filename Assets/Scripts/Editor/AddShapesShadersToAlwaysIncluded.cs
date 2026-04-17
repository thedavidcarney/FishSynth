#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor utility: adds all Shapes shaders to Always Included Shaders in Graphics Settings.
/// Run via: Tools → FishSynth → Add Shapes Shaders to Always Included
/// </summary>
public static class AddShapesShadersToAlwaysIncluded
{
    [MenuItem("Tools/FishSynth/Add Shapes Shaders to Always Included")]
    public static void AddAllShapesShaders()
    {
        // Find the Graphics Settings asset
        var graphicsSettings = AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/GraphicsSettings.asset");
        var serializedObject = new SerializedObject(graphicsSettings);
        var alwaysIncludedShaders = serializedObject.FindProperty("m_AlwaysIncludedShaders");

        // Collect existing entries so we don't duplicate
        var existing = new HashSet<Shader>();
        for (int i = 0; i < alwaysIncludedShaders.arraySize; i++)
        {
            var s = alwaysIncludedShaders.GetArrayElementAtIndex(i).objectReferenceValue as Shader;
            if (s != null) existing.Add(s);
        }

        // Find all Shapes shaders via AssetDatabase
        string[] guids = AssetDatabase.FindAssets("t:Shader", new[] { "Assets/Shapes" });
        int added = 0;
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (shader == null) continue;
            if (!shader.name.StartsWith("Shapes/")) continue;
            if (existing.Contains(shader)) continue;

            alwaysIncludedShaders.arraySize++;
            alwaysIncludedShaders.GetArrayElementAtIndex(alwaysIncludedShaders.arraySize - 1)
                .objectReferenceValue = shader;
            added++;
        }

        serializedObject.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();

        Debug.Log($"[FishSynth] Added {added} Shapes shaders to Always Included Shaders. " +
                  $"Total in list: {alwaysIncludedShaders.arraySize}");

        EditorUtility.DisplayDialog(
            "Shapes Shaders Added",
            $"Added {added} shaders to Always Included Shaders.\n\n" +
            $"Total shaders now in list: {alwaysIncludedShaders.arraySize}",
            "OK");
    }
}
#endif
