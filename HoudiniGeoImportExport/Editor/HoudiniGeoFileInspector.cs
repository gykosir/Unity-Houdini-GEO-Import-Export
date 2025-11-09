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
    [CustomEditor(typeof(Object))]
    public class HoudiniGeoFileInspector : Editor
    {
        private bool _isHoudiniGeoFile;
        private bool _fileCheckPerformed;

        private HoudiniGeo _houdiniGeo;
        private Editor _houdiniGeoInspector;

        public override void OnInspectorGUI()
        {
            switch (_isHoudiniGeoFile)
            {
                case false when !_fileCheckPerformed:
                {
                    var assetPath = AssetDatabase.GetAssetPath(target);
                    _isHoudiniGeoFile = assetPath.EndsWith("." + HoudiniGeo.Extension);
                    _fileCheckPerformed = true;
                    break;
                }
                case false:
                    return;
            }

            if (_houdiniGeo == null)
            {
                var assetPath = AssetDatabase.GetAssetPath(target);
                var outDir = Path.GetDirectoryName(assetPath);
                var assetName = Path.GetFileNameWithoutExtension(assetPath);

                // Parse geo
                var geoOutputPath = $"{outDir}/{assetName}.asset";
                _houdiniGeo = AssetDatabase.LoadAllAssetsAtPath(geoOutputPath).FirstOrDefault(a => a is HoudiniGeo) as HoudiniGeo;
                _houdiniGeoInspector = CreateEditor(_houdiniGeo);
            }

            if (_houdiniGeoInspector != null)
            {
                GUI.enabled = true;
                _houdiniGeoInspector.DrawDefaultInspector();
            }
        }
    }
}
