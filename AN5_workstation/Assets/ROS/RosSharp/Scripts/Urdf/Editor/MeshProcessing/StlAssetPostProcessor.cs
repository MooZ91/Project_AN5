/*
© Siemens AG, 2017-2019
Author: Dr. Martin Bischoff (martin.bischoff@siemens.com)

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

<http://www.apache.org/licenses/LICENSE-2.0>.

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
*/

using UnityEngine;
using UnityEditor;
using System.Linq;
using System.IO;

namespace RosSharp
{
    public class StlAssetPostProcessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromPath)
        {
            foreach (string stlFile in importedAssets.Where(x => x.ToLowerInvariant().EndsWith(".stl")))
                createStlPrefab(stlFile);
        }

        private static void createStlPrefab(string stlFile)
        {
            GameObject gameObject = CreateStlParent(stlFile);
            if (gameObject == null)
                return;

            string prefabPath = getPrefabAssetPath(stlFile);
            EnsureAssetFolder(prefabPath);
            PrefabUtility.SaveAsPrefabAsset(gameObject, prefabPath);
            Object.DestroyImmediate(gameObject);
        }

        private static GameObject CreateStlParent(string stlFile)
        {
            Mesh[] meshes = Urdf.StlImporter.ImportMesh(stlFile);
            if (meshes == null)
                return null;

            GameObject parent = new GameObject(Path.GetFileNameWithoutExtension(stlFile));
            Material material = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Diffuse.mat");
            for (int i = 0; i < meshes.Length; i++)
            {
                string meshAssetPath = getMeshAssetPath(stlFile, i);
                EnsureAssetFolder(meshAssetPath);
                if (AssetDatabase.LoadAssetAtPath<Mesh>(meshAssetPath) != null)
                    AssetDatabase.DeleteAsset(meshAssetPath);
                AssetDatabase.CreateAsset(meshes[i], meshAssetPath);
                GameObject gameObject = CreateStlGameObject(meshAssetPath, material);
                gameObject.transform.SetParent(parent.transform, false);
            }
            return parent;
        }
        private static GameObject CreateStlGameObject(string meshAssetPath, Material material)
        {
            GameObject gameObject = new GameObject(Path.GetFileNameWithoutExtension(meshAssetPath));
            gameObject.AddComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshAssetPath);
            gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
            return gameObject;
        }
        private static void EnsureAssetFolder(string assetPath)
        {
            string assetDirectory = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
            if (string.IsNullOrEmpty(assetDirectory) || AssetDatabase.IsValidFolder(assetDirectory))
                return;

            if (!assetDirectory.StartsWith("Assets/", System.StringComparison.OrdinalIgnoreCase))
                return;

            string relativeFolder = assetDirectory.Substring("Assets".Length).TrimStart('/', '\\');
            string systemPath = Path.Combine(Application.dataPath, relativeFolder);
            if (!Directory.Exists(systemPath))
                Directory.CreateDirectory(systemPath);
        }
        private static string getMeshAssetPath(string stlFile, int i)
        {
            return mapToWritableAssetPath(stlFile.Substring(0, stlFile.Length - 4) + "_" + i.ToString() + ".asset");
        }
        private static string getPrefabAssetPath(string stlFile)
        {
            return mapToWritableAssetPath(stlFile.Substring(0, stlFile.Length - 4) + ".prefab");
        }

        private static string mapToWritableAssetPath(string assetPath)
        {
            if (assetPath.StartsWith("Assets/", System.StringComparison.OrdinalIgnoreCase))
                return assetPath.Replace("\\", "/");

            const string packagesPrefix = "Packages/";
            if (assetPath.StartsWith(packagesPrefix, System.StringComparison.OrdinalIgnoreCase))
            {
                string relativePath = assetPath.Substring(packagesPrefix.Length);
                string localPath = Path.Combine("Assets", "GeneratedURDFMeshes", relativePath);
                return localPath.Replace("\\", "/");
            }

            return assetPath.Replace("\\", "/");
        }
    }
}