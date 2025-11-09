/**
 * Houdini Geo File Importer for Unity
 *
 * Copyright 2015 by Waldo Bronchart <wbronchart@gmail.com>
 * Licensed under GNU General Public License 3.0 or later. 
 * Some rights reserved. See COPYING, AUTHORS.
 */

using System;
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Newtonsoft.Json.Linq;

namespace Houdini.GeoImportExport
{
    public class HoudiniGeoParseException : Exception
    {
        public HoudiniGeoParseException(string message) : base(message)
        {

        }
    }

    public static class HoudiniGeoFileParser
    {
        public static HoudiniGeo Parse(string assetPath)
        {
            return ParseInternal(assetPath, null);
        }

        public static void ParseInto(string assetPath, HoudiniGeo existingGeo)
        {
            ParseInternal(assetPath, existingGeo);
        }

        private static HoudiniGeo ParseInternal(string assetPath, HoudiniGeo existingGeo = null)
        {
            if (!File.Exists(assetPath))
            {
                throw new FileNotFoundException("File not found: " + assetPath);
            }

            // Parse the json
            JToken mainToken;
            try
            {
                mainToken = JToken.Parse(File.ReadAllText(assetPath));
            }
            catch (Exception)
            {
                Debug.LogError($"HoudiniGeoParseError: JSON in file '{assetPath}' could not be parsed");
                throw;
            }

            // The houdini geo format expects the main element to be an array
            if (mainToken.Type != JTokenType.Array)
            {
                throw new HoudiniGeoParseException("Unexpected type in geo json.");
            }

            // The main element is an array that actually functions as a dictionary!
            var geoDataDict = ArrayKeyValueToDictionary(mainToken.Children().ToArray());

            var houdiniGeo = existingGeo;
            if (houdiniGeo == null)
            {
                houdiniGeo = ScriptableObject.CreateInstance<HoudiniGeo>();
            }
            houdiniGeo.sourceAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);

            houdiniGeo.fileVersion = geoDataDict["fileversion"].ValueSafe<string>();

            houdiniGeo.hasIndex = geoDataDict.TryGetValue("hasIndex", out var hasIndexToken) && hasIndexToken.ValueSafe<bool>();

            houdiniGeo.pointCount = geoDataDict["pointcount"].ValueSafe<int>();
            houdiniGeo.vertexCount = geoDataDict["vertexcount"].ValueSafe<int>();
            houdiniGeo.primCount = geoDataDict["primitivecount"].ValueSafe<int>();

            houdiniGeo.fileInfo = ParseFileInfo(geoDataDict["info"] as JObject);

            ParseTopology(houdiniGeo, geoDataDict["topology"]);
            ParseAttributes(houdiniGeo, geoDataDict["attributes"]);
            ParsePrimitives(houdiniGeo, geoDataDict["primitives"]);

            return houdiniGeo;
        }

        private static HoudiniGeoFileInfo ParseFileInfo(JObject infoValueToken)
        {
            var fileInfo = new HoudiniGeoFileInfo();

            if (infoValueToken == null)
                throw new HoudiniGeoParseException("Missing 'info' section in geo file");

            fileInfo.software = infoValueToken.TryGetValue("software", out var softwareToken)
                ? softwareToken.Value<string>()
                : "Unknown";
            fileInfo.hostname = infoValueToken.TryGetValue("hostname", out var hostnameToken)
                ? hostnameToken.Value<string>()
                : "Unknown";
            fileInfo.artist = infoValueToken.TryGetValue("artist", out var artistToken)
                ? artistToken.Value<string>()
                : "Unknown";
            fileInfo.timetocook = infoValueToken.TryGetValue("timetocook", out var timeToken)
                ? timeToken.Value<float>()
                : 0f;
            fileInfo.date = infoValueToken.TryGetValue("date", out var dateToken)
                ? dateToken.Value<DateTime>()
                : DateTime.MinValue;

            if (infoValueToken.TryGetValue("bounds", out var boundsToken))
            {
                var bv = boundsToken.Values<float>().ToArray();
                if (bv.Length == 6)
                {
                    var boundsMin = new Vector3(bv[0], bv[1], bv[2]);
                    var boundsMax = new Vector3(bv[3], bv[4], bv[5]);
                    fileInfo.bounds = new Bounds();
                    fileInfo.bounds.SetMinMax(boundsMin, boundsMax);
                }
            }

            var hadPrimCountSummary = infoValueToken.TryGetValue("primcount_summary", out var primcountSummary);
            if (hadPrimCountSummary)
                fileInfo.primcount_summary = primcountSummary.Value<string>();

            fileInfo.attribute_summary = infoValueToken.TryGetValue("attribute_summary", out var attrSummaryToken)
                ? attrSummaryToken.Value<string>()
                : "";

            return fileInfo;
        }

        private static void ParseTopology(HoudiniGeo geo, JToken topologyValueToken)
        {
            if (topologyValueToken == null)
                throw new HoudiniGeoParseException("Missing 'topology' section in geo file");

            var topologyDict = ArrayKeyValueToDictionary(topologyValueToken.Children().ToArray());

            if (!topologyDict.TryGetValue("pointref", out var pointRefToken))
                throw new HoudiniGeoParseException("Missing 'pointref' in topology");

            var pointRefDict = ArrayKeyValueToDictionary(pointRefToken.Children().ToArray());

            if (!pointRefDict.TryGetValue("indices", out var indicesToken))
                throw new HoudiniGeoParseException("Missing 'indices' in pointref");

            geo.pointRefs = indicesToken.Values<int>().ToList();
        }

        private static Dictionary<string, HoudiniGeoAttributeOwner> _cachedAttributesToParse;
        public static Dictionary<string, HoudiniGeoAttributeOwner> attributesToParse
        {
            get
            {
                _cachedAttributesToParse ??= new Dictionary<string, HoudiniGeoAttributeOwner>
                {
                    {"vertexattributes", HoudiniGeoAttributeOwner.Vertex},
                    {"pointattributes", HoudiniGeoAttributeOwner.Point},
                    {"primitiveattributes", HoudiniGeoAttributeOwner.Primitive},
                    {"globalattributes", HoudiniGeoAttributeOwner.Detail}
                };
                return _cachedAttributesToParse;
            }
        }


        private static void ParseAttributes(HoudiniGeo geo, JToken attributesValueToken)
        {
            // "attributes",[
            // 		"vertexattributes",[
            //			[(attribute obj)],
            //			[(attribute obj)],
            //			...
            // 		],
            // 		"pointattributes",[
            //			[(attribute obj)],
            //			[(attribute obj)],
            //			...
            // 		],
            //		...
            // ],

            var attributeTokensDict = ArrayKeyValueToDictionary(attributesValueToken.Children().ToArray());

            // Parse each attribute group
            var geoAttributes = new List<HoudiniGeoAttribute>();
            foreach (var (attrGroupKey, attrOwner) in attributesToParse)
            {
                if (!attributeTokensDict.TryGetValue(attrGroupKey, out var groupValueToken)) continue;
                // Parse each attribute in group
                foreach (var attributeToken in groupValueToken.Children())
                {
                    var attribute = ParseSingleAttribute(attributeToken, attrOwner);
                    if (attribute != null) geoAttributes.Add(attribute);
                }
            }

            geo.attributes.AddRange(geoAttributes);
        }

        private static HoudiniGeoAttribute ParseSingleAttribute(JToken attrToken, HoudiniGeoAttributeOwner owner)
        {
            var childBlockTokens = attrToken.Children().ToArray();

            if (childBlockTokens.Length < 2)
            {
                Debug.LogWarning("HoudiniGeoFileParser: attribute token has insufficient child blocks");
                return null;
            }

            var headerToken = childBlockTokens[0];
            var bodyToken = childBlockTokens[1];

            var geoAttribute = new HoudiniGeoAttribute
            {
                owner = owner
            };

            // Parse header block
            var headerBlockDict = ArrayKeyValueToDictionary(headerToken.Children().ToArray());
            geoAttribute.name = headerBlockDict["name"].Value<string>();
            var valueType = headerBlockDict["type"].Value<string>();

            // Parse body block
            var valuesBlockDict = ArrayKeyValueToDictionary(bodyToken.Children().ToArray());
            geoAttribute.tupleSize = valuesBlockDict["size"].Value<int>();

            // Parse Numeric types
            if (valueType == "numeric")
            {
                // Get storage type (float, int)
                var storageType = valuesBlockDict["storage"].Value<string>();
                geoAttribute.type = AttributeTypeStrToEnumValue(storageType);
                if (geoAttribute.type == HoudiniGeoAttributeType.Invalid)
                {
                    Debug.LogWarning("HoudiniGeoFileParser: unsuppored numeric storage type " + valueType);
                    return null;
                }

                // Get all values
                var valuesDict = ArrayKeyValueToDictionary(valuesBlockDict["values"].Children().ToArray());
                if (geoAttribute.type == HoudiniGeoAttributeType.Float)
                {
                    var tupleSize = valuesDict["size"].Value<int>();
                    var valuesKey = (tupleSize == 1) ? "arrays" : "tuples";
                    geoAttribute.floatValues = valuesDict[valuesKey].Children().SelectMany(t => t.Values<float>()).ToList();
                }
                else if (geoAttribute.type == HoudiniGeoAttributeType.Integer)
                {
                    geoAttribute.intValues = valuesDict["arrays"].Children().SelectMany(t => t.Values<int>()).ToList();
                }
            }
            // Parse String types
            else if (valueType == "string")
            {
                geoAttribute.type = HoudiniGeoAttributeType.String;

                var indicesDict = ArrayKeyValueToDictionary(valuesBlockDict["indices"].Children().ToArray());
                var stringValues = valuesBlockDict["strings"].Values<string>().ToArray();
                var indices = indicesDict["arrays"].Children().SelectMany(t => t.Values<int>()).ToArray();

                geoAttribute.stringValues = indices.Select(i => (i >= 0 && i < stringValues.Length) ? stringValues[i] : "").ToList();
            }
            // Unexpected type?
            else
            {
                Debug.LogWarning("HoudiniGeoFileParser: unsuppored attribute valueType " + valueType);
                return null;
            }

            return geoAttribute;
        }



        private static void ParsePrimitives(HoudiniGeo geo, JToken primitivesValueToken)
        {
            if (primitivesValueToken == null)
            {
                Debug.LogWarning("HoudiniGeoFileParser: Missing 'primitives' section");
                return;
            }

            const int primIdCounter = 0;

            foreach (var primitiveToken in primitivesValueToken.Children())
            {
                var childBlockTokens = primitiveToken.Children().ToArray();

                if (childBlockTokens.Length < 2)
                {
                    Debug.LogWarning("HoudiniGeoFileParser: primitive token has insufficient child blocks");
                    continue;
                }

                var headerToken = childBlockTokens[0];
                var bodyToken = childBlockTokens[1];

                // Parse header
                var headerDict = ArrayKeyValueToDictionary(headerToken.Children().ToArray());

                if (!headerDict.TryGetValue("type", out var typeToken))
                {
                    Debug.LogWarning("HoudiniGeoFileParser: primitive missing 'type' field");
                    continue;
                }

                var type = typeToken.Value<string>();

                // Parse RunType primitives
                if (type == "run")
                {
                    if (!headerDict.TryGetValue("runtype", out var runTypeToken))
                    {
                        Debug.LogWarning("HoudiniGeoFileParser: run primitive missing 'runtype' field");
                        continue;
                    }

                    var runType = runTypeToken.Value<string>();
                    switch (runType)
                    {
                        case "Poly":
                            geo.polyPrimitives.AddRange(ParsePolyPrimitiveGroup(headerDict, bodyToken, primIdCounter));
                            break;
                        case "BezierCurve":
                            //geo.bezierCurvePrimitives.AddRange(primitives);
                            break;
                        case "NURBCurve":
                            //geo.nurbCurvePrimitives.AddRange(primitives);
                            break;
                    }
                }
            }
        }

        private static PolyPrimitive[] ParsePolyPrimitiveGroup(Dictionary<string, JToken> headerDict, JToken bodyToken, int primIdCounter)
        {
            if (bodyToken == null)
                return Array.Empty<PolyPrimitive>();

            return bodyToken.Children().Select(primToken =>
            {
                var primChildTokens = primToken.Children().ToArray();

                if (primChildTokens.Length == 0)
                    return null;

                var indices = primChildTokens[0].Values<int>().ToArray();
                var prim = new PolyPrimitive
                {
                    id = primIdCounter++,
                    indices = indices,
                    triangles = TriangulateNGon(indices)
                };
                return prim;
            }).Where(p => p != null).ToArray();
        }

        private static int[] TriangulateNGon(int[] indices)
        {
            if (indices.Length <= 3)
            {
                return indices;
            }

            // Naive triangulation! Does not work for convex ngons
            var triangles = new List<int>();
            for (var offset = 1; offset < indices.Length - 1; offset++)
            {
                triangles.Add(indices[0]);
                triangles.Add(indices[offset]);
                triangles.Add(indices[offset + 1]);
            }
            return triangles.ToArray();
        }

        private static T ValueSafe<T>(this JToken jToken)
        {
            try
            {
                return jToken.Value<T>();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                throw new HoudiniGeoParseException(
                    $"Expecting property value of type '{typeof(T).Name}' but found '{jToken.Type}' instead");
            }
        }

        private static Dictionary<string, JToken> ArrayKeyValueToDictionary(JToken[] tokens)
        {
            var tokenDictionary = new Dictionary<string, JToken>();

            for (var i = 0; i < tokens.Length; i += 2)
            {
                var keyToken = tokens[i];
                var valueToken = tokens[i + 1];
                tokenDictionary.Add(keyToken.Value<string>(), valueToken);
            }

            return tokenDictionary;
        }

        public static HoudiniGeoAttributeType AttributeTypeStrToEnumValue(string typeStr)
        {
            switch (typeStr.ToLower())
            {
                case "int32":
                    return HoudiniGeoAttributeType.Integer;
                case "fpreal32":
                case "fpreal64":
                    return HoudiniGeoAttributeType.Float;
                case "string":
                    return HoudiniGeoAttributeType.String;
                default:
                    throw new HoudiniGeoParseException("Unexpected attribute type: " + typeStr);
            }
        }

        public static string AttributeEnumValueToTypeStr(HoudiniGeoAttributeType enumValue)
        {
            switch (enumValue)
            {
                case HoudiniGeoAttributeType.Integer:
                    return "int32";
                case HoudiniGeoAttributeType.Float:
                    return "fpreal64"; // NOTE: Don't know whether to use fpreal32 or fpreal64 so just using 64 for now.
                case HoudiniGeoAttributeType.String:
                    return "string";
                default:
                    throw new HoudiniGeoParseException("Unexpected attribute type: " + enumValue);
            }
        }

        public static string AttributeTypeEnumValueToCategoryString(HoudiniGeoAttributeType enumValue)
        {
            string typeString;
            switch (enumValue)
            {
                case HoudiniGeoAttributeType.Float:
                case HoudiniGeoAttributeType.Integer:
                    typeString = "numeric";
                    break;
                case HoudiniGeoAttributeType.String:
                    typeString = "string";
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            return typeString;
        }
    }
}
