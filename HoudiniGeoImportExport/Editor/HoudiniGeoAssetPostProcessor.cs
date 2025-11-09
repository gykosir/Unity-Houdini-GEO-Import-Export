/**
 * Houdini Geo File Importer for Unity
 *
 * Copyright 2015 by Waldo Bronchart <wbronchart@gmail.com>
 * Licensed under GNU General Public License 3.0 or later. 
 * Some rights reserved. See COPYING, AUTHORS.
 */

using UnityEngine;
using UnityEditor;
using System.Linq;
using System.IO;

namespace Houdini.GeoImportExport
{
    public class HoudiniGeoAssetPostProcessor : AssetPostprocessor
    {
        private static bool IsHoudiniGeoFile(string path)
        {
            return path.ToLower().EndsWith(".geo");
        }

        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            var houdiniGeosImported = importedAssets.Where(IsHoudiniGeoFile).ToArray();

            foreach (var assetPath in houdiniGeosImported)
            {
                //Debug.Log("Importing: " + assetPath);

                var outDir = Path.GetDirectoryName(assetPath);
                var assetName = Path.GetFileNameWithoutExtension(assetPath);

                // Parse geo
                var geoOutputPath = $"{outDir}/{assetName}.asset";
                var houdiniGeo = AssetDatabase.LoadAllAssetsAtPath(geoOutputPath).FirstOrDefault(a => a is HoudiniGeo) as HoudiniGeo;
                if (houdiniGeo == null)
                {
                    houdiniGeo = ScriptableObject.CreateInstance<HoudiniGeo>();
                    AssetDatabase.CreateAsset(houdiniGeo, geoOutputPath);
                }
                else
                {
                    // Make sure that it's empty so it can receive the new info.
                    houdiniGeo.Clear();
                }

                HoudiniGeoFileParser.ParseInto(assetPath, houdiniGeo);

                houdiniGeo.ImportAllMeshes();

                EditorUtility.SetDirty(houdiniGeo);
                
                HoudiniGeo.DispatchGeoFileImportedEvent(houdiniGeo);
            }

            if (houdiniGeosImported.Length > 0)
            {
                AssetDatabase.SaveAssets();
            }
        }
    }
}
