/**
 * Houdini Geo File Importer for Unity
 *
 * Copyright 2015 by Waldo Bronchart <wbronchart@gmail.com>
 * Exporter added in 2021 by Roy Theunissen <roy.theunissen@live.nl>
 * Licensed under GNU General Public License 3.0 or later.
 * Some rights reserved. See COPYING, AUTHORS.
 */

using UnityEngine;
using UnityEditor;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;

namespace Houdini.GeoImportExport
{
    public static class HoudiniGeoExtensions
    {
        public const string PositionAttributeName = "P";
        public const string NormalAttributeName = "N";
        public const string UpAttributeName = "up";
        public const string RotationAttributeName = "orient";

        private static readonly string[] GroupFieldNames = { "groups", "grouping" };
        
        internal static void ImportAllMeshes(this HoudiniGeo geo)
        {
            var geoAssetPath = AssetDatabase.GetAssetPath(geo);
            if (!File.Exists(geoAssetPath))
            {
                return;
            }

            // Convert to unity mesh and store mesh as sub asset
            if (geo.polyPrimitives.Count <= 0) return;
            var mesh = AssetDatabase.LoadAllAssetsAtPath(geoAssetPath).FirstOrDefault(a => a is Mesh) as Mesh;
            if (mesh == null)
            {
                mesh = new Mesh();
                AssetDatabase.AddObjectToAsset(mesh, geoAssetPath);
            }
                
            geo.ToUnityMesh(mesh);
            EditorUtility.SetDirty(mesh);
        }

        public static void ToUnityMesh(this HoudiniGeo geo, Mesh mesh)
        {
            if (geo.polyPrimitives.Count == 0)
            {
                Debug.LogError("Cannot convert HoudiniGeo to Mesh because geo has no PolyPrimitives");
                return;
            }

            mesh.name = geo.name;
            var indices = geo.polyPrimitives.SelectMany(p => p.indices).ToArray();
            var vertexCount = indices.Length;
            if (vertexCount > 65000)
            {
                throw new Exception($"Vertex count ({geo.vertexCount}) exceeds limit of {65000}!");
            }

            // Check if position attribute P exists
            if (!geo.TryGetAttribute(HoudiniGeo.PosAttrName, HoudiniGeoAttributeType.Float, out var posAttr))
            {
                Debug.LogWarning("HoudiniGEO has no Position attribute on points or vertices");
            }

            // Get Vertex/Point positions
            posAttr.GetValues(out Vector3[] posAttrValues);
            
            // Get uv attribute values
            Vector2[] uvAttrValues = null;
            if (geo.TryGetAttribute(HoudiniGeo.UVAttrName, HoudiniGeoAttributeType.Float, out var uvAttr))
            {
                uvAttr.GetValues(out uvAttrValues);
            }
            
            // Get uv2 attribute values
            Vector2[] uv2AttrValues = null;
            if (geo.TryGetAttribute(HoudiniGeo.UV2AttrName, HoudiniGeoAttributeType.Float, out var uv2Attr))
            {
                uv2Attr.GetValues(out uv2AttrValues);
            }
            
            // Get normal attribute values
            Vector3[] normalAttrValues = null;
            if (geo.TryGetAttribute(HoudiniGeo.NormalAttrName, HoudiniGeoAttributeType.Float, out var normalAttr))
            {
                normalAttr.GetValues(out normalAttrValues);
            }
            
            // Get color attribute values
            Color[] colorAttrValues = null;
            if (geo.TryGetAttribute(HoudiniGeo.ColorAttrName, HoudiniGeoAttributeType.Float, out var colorAttr))
            {
                colorAttr.GetValues(out colorAttrValues);

                // Get alpha color values
                if (geo.TryGetAttribute(HoudiniGeo.AlphaAttrName, HoudiniGeoAttributeType.Float, colorAttr.owner, out var alphaAttr))
                {
                    alphaAttr.GetValues(out float[] alphaAttrValues);

                    if (colorAttrValues.Length == alphaAttrValues.Length)
                    {
                        for (var i=0; i<colorAttrValues.Length; i++)
                        {
                            colorAttrValues[i].a = alphaAttrValues[i];
                        }
                    }
                }
            }
            
            // Get tangent attribute values
            Vector3[] tangentAttrValues = null;
            if (geo.TryGetAttribute(HoudiniGeo.TangentAttrName, HoudiniGeoAttributeType.Float, out var tangentAttr))
            {
                tangentAttr.GetValues(out tangentAttrValues);
            }

            // Get material primitive attribute (Multiple materials result in multiple submeshes)
            string[] materialAttributeValues = null;
            if (geo.TryGetAttribute(HoudiniGeo.MaterialAttrName, HoudiniGeoAttributeType.String, HoudiniGeoAttributeOwner.Primitive, out var materialAttr))
            {
                materialAttr.GetValues(out materialAttributeValues);
            }

            // Create our mesh attribute buffers
            var submeshInfo = new Dictionary<string, List<int>>();
            var positions = new Vector3[vertexCount];
            var uvs = new Vector2[vertexCount]; // unity doesn't like it when meshes have no uvs
            var uvs2 = (uv2Attr != null) ? new Vector2[vertexCount] : null;
            var normals = (normalAttr != null) ? new Vector3[vertexCount] : null;
            var colors = (colorAttr != null) ? new Color[vertexCount] : null;
            var tangents = (tangentAttr != null) ? new Vector4[vertexCount] : null;

            // Fill the mesh buffers
            var vertToPoint = geo.pointRefs.ToArray();
            var vertIndexGlobalToLocal = new Dictionary<int, int>();
            for (var i=0; i<vertexCount; ++i)
            {
                var vertIndex = indices[i];
                var pointIndex = vertToPoint[vertIndex];
                vertIndexGlobalToLocal.Add(vertIndex, i);
                
                // Position
                positions[i] = posAttr.owner switch
                {
                    HoudiniGeoAttributeOwner.Vertex => posAttrValues[vertIndex],
                    HoudiniGeoAttributeOwner.Point => posAttrValues[pointIndex],
                    _ => positions[i]
                };

                // UV1
                if (uvAttr != null)
                {
                    uvs[i] = uvAttr.owner switch
                    {
                        HoudiniGeoAttributeOwner.Vertex => uvAttrValues[vertIndex],
                        HoudiniGeoAttributeOwner.Point => uvAttrValues[pointIndex],
                        _ => uvs[i]
                    };
                }
                else
                {
                    // Unity likes to complain when a mesh doesn't have any UVs so we'll just add a default
                    uvs[i] = Vector2.zero;
                }
                
                // UV2
                if (uv2Attr != null)
                {
                    uvs2[i] = uv2Attr.owner switch
                    {
                        HoudiniGeoAttributeOwner.Vertex => uv2AttrValues[vertIndex],
                        HoudiniGeoAttributeOwner.Point => uv2AttrValues[pointIndex],
                        _ => uvs2[i]
                    };
                }
                
                // Normals
                if (normalAttr != null)
                {
                    normals[i] = normalAttr.owner switch
                    {
                        HoudiniGeoAttributeOwner.Vertex => normalAttrValues[vertIndex],
                        HoudiniGeoAttributeOwner.Point => normalAttrValues[pointIndex],
                        _ => normals[i]
                    };
                }
                
                // Colors
                if (colorAttr != null)
                {
                    colors[i] = colorAttr.owner switch
                    {
                        HoudiniGeoAttributeOwner.Vertex => colorAttrValues[vertIndex],
                        HoudiniGeoAttributeOwner.Point => colorAttrValues[pointIndex],
                        _ => colors[i]
                    };
                }
                
                // Fill tangents info
                if (tangentAttr != null)
                {
                    tangents[i] = tangentAttr.owner switch
                    {
                        HoudiniGeoAttributeOwner.Vertex => tangentAttrValues[vertIndex],
                        HoudiniGeoAttributeOwner.Point => tangentAttrValues[pointIndex],
                        _ => tangents[i]
                    };
                }
            }

            // Get primitive attribute values and created submeshes
            foreach (var polyPrim in geo.polyPrimitives)
            {
                // Normals
                if (normalAttr is {owner: HoudiniGeoAttributeOwner.Primitive})
                {
                    foreach (var vertIndex in polyPrim.indices)
                    {
                        var localVertIndex = vertIndexGlobalToLocal[vertIndex];
                        normals[localVertIndex] = normalAttrValues[polyPrim.id];
                    }
                }

                // Colors
                if (colorAttr is {owner: HoudiniGeoAttributeOwner.Primitive})
                {
                    foreach (var vertIndex in polyPrim.indices)
                    {
                        var localVertIndex = vertIndexGlobalToLocal[vertIndex];
                        colors[localVertIndex] = colorAttrValues[polyPrim.id];
                    }
                }

                // Add face to submesh based on material attribute
                var materialName = (materialAttr == null) ? HoudiniGeo.DefaultMaterialName : materialAttributeValues[polyPrim.id];
                if (!submeshInfo.ContainsKey(materialName))
                {
                    submeshInfo.Add(materialName, new List<int>());
                }
                submeshInfo[materialName].AddRange(polyPrim.triangles);
            }

            // Assign buffers to mesh
            mesh.vertices = positions;
            mesh.subMeshCount = submeshInfo.Count;
            mesh.uv = uvs;
            mesh.uv2 = uvs2;
            mesh.normals = normals;
            mesh.colors = colors;
            mesh.tangents = tangents;
            
            // Set submesh indexbuffers
            var submeshIndex = 0;
            foreach (var item in submeshInfo)
            {
                // Skip empty submeshes
                if (item.Value.Count == 0)
                    continue;
                
                // Set the indices for the submesh (Reversed by default because axis coordinates Z flipped)
                IEnumerable<int> submeshIndices = item.Value;
                if (!geo.importSettings.reverseWinding)
                {
                    submeshIndices = submeshIndices.Reverse();
                }
                mesh.SetIndices(submeshIndices.ToArray(), MeshTopology.Triangles, submeshIndex);
                
                submeshIndex++;
            }

            // Calculate any missing buffers
            mesh.ConvertToUnityCoordinates();
            mesh.RecalculateBounds();
            if (normalAttr == null)
            {
                mesh.RecalculateNormals();
            }
        }

        private static void ConvertToUnityCoordinates(this Mesh mesh)
        {
            var vertices = mesh.vertices;
            for (var i=0; i<vertices.Length; i++)
            {
                vertices[i].z *= -1;
            }
            mesh.vertices = vertices;

            var normals = mesh.normals;
            for (var i=0; i<normals.Length; i++)
            {
                normals[i].z *= -1;
            }
            mesh.normals = normals;
            
            var tangents = mesh.tangents;
            for (var i=0; i<tangents.Length; i++)
            {
                tangents[i].z *= -1;
            }
            mesh.tangents = tangents;
        }

        public static bool HasAttribute(this HoudiniGeo geo, string attrName, HoudiniGeoAttributeOwner owner)
        {
            return owner == HoudiniGeoAttributeOwner.Any 
                ? geo.attributes.Any(a => a.name == attrName) 
                : geo.attributes.Any(a => a.owner == owner && a.name == attrName);
        }
        
        public static bool TryGetAttribute(this HoudiniGeo geo, string attrName, out HoudiniGeoAttribute attr)
        {
            attr = geo.attributes.FirstOrDefault(a => a.name == attrName);
            return (attr != null);
        }
        
        public static bool TryGetAttribute(this HoudiniGeo geo, string attrName, HoudiniGeoAttributeType type, out HoudiniGeoAttribute attr)
        {
            attr = geo.attributes.FirstOrDefault(a => a.type == type && a.name == attrName);
            return (attr != null);
        }

        public static bool TryGetAttribute(this HoudiniGeo geo, string attrName, HoudiniGeoAttributeOwner owner, out HoudiniGeoAttribute attr)
        {
            if (owner == HoudiniGeoAttributeOwner.Any)
            {
                attr = geo.attributes.FirstOrDefault(a => a.name == attrName);
            }
            else
            {
                attr = geo.attributes.FirstOrDefault(a => a.owner == owner && a.name == attrName);
            }

            return (attr != null);
        }
        
        public static bool TryGetAttribute(this HoudiniGeo geo, string attrName, HoudiniGeoAttributeType type,
                                           HoudiniGeoAttributeOwner owner, out HoudiniGeoAttribute attr)
        {
            if (owner == HoudiniGeoAttributeOwner.Any)
            {
                attr = geo.attributes.FirstOrDefault(a => a.type == type && a.name == attrName);
            }
            else
            {
                attr = geo.attributes.FirstOrDefault(a => a.owner == owner && a.type == type && a.name == attrName);
            }
            return (attr != null);
        }

        private static void GetValues(this HoudiniGeoAttribute attr, out float[] values)
        {
            if (!attr.ValidateForGetValues<float>(HoudiniGeoAttributeType.Float, 1))
            {
                values = Array.Empty<float>();
                return;
            }

            values = attr.floatValues.ToArray();
        }
        
        private static void GetValues(this HoudiniGeoAttribute attr, out Vector2[] values)
        {
            if (!attr.ValidateForGetValues<Vector2>(HoudiniGeoAttributeType.Float, 2))
            {
                values = Array.Empty<Vector2>();
                return;
            }
            
            // Convert to Vector2
            var rawValues = attr.floatValues.ToArray();
            values = new Vector2[rawValues.Length / attr.tupleSize];
            for (var i=0; i<values.Length; i++)
            {
                values[i].x = rawValues[i * attr.tupleSize];
                values[i].y = rawValues[i * attr.tupleSize + 1];
            }
        }

        private static void GetValues(this HoudiniGeoAttribute attr, out Vector3[] values)
        {
            if (!attr.ValidateForGetValues<Vector3>(HoudiniGeoAttributeType.Float, 3))
            {
                values = Array.Empty<Vector3>();
                return;
            }

            // Convert to Vector3
            var rawValues = attr.floatValues.ToArray();
            values = new Vector3[rawValues.Length / attr.tupleSize];
            for (var i=0; i<values.Length; i++)
            {
                values[i].x = rawValues[i * attr.tupleSize];
                values[i].y = rawValues[i * attr.tupleSize + 1];
                values[i].z = rawValues[i * attr.tupleSize + 2];
            }
        }
        
        private static void GetValues(this HoudiniGeoAttribute attr, out Vector4[] values)
        {
            if (!attr.ValidateForGetValues<Vector4>(HoudiniGeoAttributeType.Float, 4))
            {
                values = Array.Empty<Vector4>();
                return;
            }
            
            // Convert to Vector4
            var rawValues = attr.floatValues.ToArray();
            values = new Vector4[rawValues.Length / attr.tupleSize];
            for (var i=0; i<values.Length; i++)
            {
                values[i].x = rawValues[i * attr.tupleSize];
                values[i].y = rawValues[i * attr.tupleSize + 1];
                values[i].z = rawValues[i * attr.tupleSize + 2];
                values[i].w = rawValues[i * attr.tupleSize + 3];
            }
        }
        
        private static void GetValues(this HoudiniGeoAttribute attr, out Color[] values)
        {
            if (!attr.ValidateForGetValues<Color>(HoudiniGeoAttributeType.Float, 3))
            {
                values = Array.Empty<Color>();
                return;
            }
            
            // Convert to Color
            var rawValues = attr.floatValues.ToArray();
            values = new Color[rawValues.Length / attr.tupleSize];
            for (var i=0; i<values.Length; i++)
            {
                values[i].r = rawValues[i * attr.tupleSize];
                values[i].g = rawValues[i * attr.tupleSize + 1];
                values[i].b = rawValues[i * attr.tupleSize + 2];
                values[i].a = 1;
                if (attr.tupleSize == 4)
                {
                    values[i].a = rawValues[i * attr.tupleSize + 3];
                }
            }
        }

        private static void GetValues(this HoudiniGeoAttribute attr, out int[] values)
        {
            if (!attr.ValidateForGetValues<int>(HoudiniGeoAttributeType.Integer, 1))
            {
                values = Array.Empty<int>();
                return;
            }
            
            values = attr.intValues.ToArray();
        }
        
        private static void GetValues(this HoudiniGeoAttribute attr, out string[] values)
        {
            if (!attr.ValidateForGetValues<string>(HoudiniGeoAttributeType.String, 1))
            {
                values = Array.Empty<string>();
                return;
            }
            
            values = attr.stringValues.ToArray();
        }
        
        private static bool ValidateForGetValues<T>(this HoudiniGeoAttribute attr, HoudiniGeoAttributeType expectedType, 
                                                    int expectedMinTupleSize)
        {
            if (attr.type != expectedType)
            {
                Debug.LogError(
                    $"Cannot convert raw values of {attr.owner} attribute '{attr.name}' to {typeof(T).Name} (type: {attr.type})");
                return false;
            }
            
            if (attr.tupleSize < expectedMinTupleSize)
            {
                Debug.LogError(
                    $"The tuple size of {attr.owner} attribute '{attr.name}' too small for conversion to {typeof(T).Name}");
                return false;
            }
            
            return true;
        }
        
        public static void Export(this HoudiniGeo houdiniGeo, string path = null)
        {
            HoudiniGeoFileExporter.Export(houdiniGeo, path);
        }

        public static void AddPoints<PointType>(
            this HoudiniGeo houdiniGeo, PointCollection<PointType> pointCollection,
            bool translateCoordinateSystems = true)
            where PointType : PointData
        {
            // First create the attributes.
            var fieldToPointAttribute =
                new Dictionary<FieldInfo, HoudiniGeoAttribute>();
            var fieldToDetailAttribute = new Dictionary<FieldInfo, HoudiniGeoAttribute>();
            var pointType = typeof(PointType);
            var fieldCandidates =
                pointType.GetFields(
                    BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            FieldInfo groupsField = null;
            foreach (var field in fieldCandidates)
            {
                // Ignore private fields that aren't tagged with SerializeField. 
                if (field.IsPrivate && field.GetCustomAttribute<SerializeField>() == null)
                    continue;

                if (GroupFieldNames.Contains(field.Name))
                {
                    groupsField = field;
                    continue;
                }

                var owner =
                    field.IsStatic ? HoudiniGeoAttributeOwner.Detail : HoudiniGeoAttributeOwner.Point;

                var hasValidAttribute = TryGetOrCreateAttribute(
                    houdiniGeo, field, owner, out var attribute);
                if (!hasValidAttribute)
                    continue;

                if (field.IsStatic)
                    fieldToDetailAttribute.Add(field, attribute);
                else
                    fieldToPointAttribute.Add(field, attribute);
            }
            
            // Now increment the point count. We must do this AFTER the attributes are created because if there are
            // already points and we create a new attribute, it will neatly populate the value collections with default
            // values for those pre-existing points. These new points will receive such attribute values down below.
            houdiniGeo.pointCount += pointCollection.Count;

            // Then populate the point attributes with values.
            foreach (var (key, attribute) in fieldToPointAttribute)
            {
                foreach (var point in pointCollection)
                {
                    var value = key.GetValue(point);
                    attribute.AddValueAsTuples(value, translateCoordinateSystems);
                }
            }
            
            // Now populate the detail attributes with values.
            foreach (var (key, attribute) in fieldToDetailAttribute)
            {
                var value = key.GetValue(null);
                attribute.AddValueAsTuples(value, translateCoordinateSystems);
            }

            // Figure out which groups this point has based on the enum type.
            if (groupsField != null)
            {
                var groupsEnumType = groupsField.FieldType;
                if (!typeof(Enum).IsAssignableFrom(groupsEnumType))
                {
                    Debug.LogError($"Fields named 'groups' are special and are used to set groups in the .GEO file. " +
                                   $"It must be of an enum type with each flag representing its group participation.");
                    return;
                }

                // Now create a group for every flag in the enum.
                var enumNames = Enum.GetNames(groupsEnumType);
                var enumValues = Enum.GetValues(groupsEnumType);
                for (var i = 0; i < enumNames.Length; i++)
                {
                    var groupName = enumNames[i];
                    var groupValue = (int)enumValues.GetValue(i);
                    
                    if (groupValue <= 0)
                        continue;

                    // Get or create the point group.
                    houdiniGeo.TryGetOrCreateGroup(groupName, HoudiniGeoGroupType.Points, out PointGroup group);
                    
                    // Populate the group with points.
                    for (var j = 0; j < pointCollection.Count; j++)
                    {
                        var point = pointCollection[j];
                        var pointGroupFlags = (int)groupsField.GetValue(point);

                        var pointId = houdiniGeo.pointCount - pointCollection.Count + j;
                        
                        // Check that the point has the flag for this group.
                        if ((pointGroupFlags & groupValue) == groupValue)
                            group.ids.Add(pointId);
                    }

                    // Now add it to the geometry.
                    houdiniGeo.PointGroups.Add(group);
                }
            }
        }

        public static void AddSplines<SplineType, PointType>(
            this HoudiniGeo houdiniGeo, SplineCollection<SplineType> splineCollection,
            bool translateCoordinateSystems = true)
            where SplineType : SplineData<PointType>
            where PointType : PointData
        {
            foreach (var spline in splineCollection)
            {
                // Firstly we can just add all the points. There is nothing special about these points, it's like
                // any ordinary point collection.
                houdiniGeo.AddPoints(spline.points, translateCoordinateSystems);

                // Primitives are comprised of vertices, not points. So we need to add a vertex for every point.
                // Why do we do this in reverse? The splines I exported from Maya do it in reverse, so I'm doing it too
                // for consistency. You'll get the spline served to you the way it would be if it came from Maya instead.
                for (var i = spline.points.Count - 1; i >= 0; i--)
                {
                    // Knowing that we just added points, we can grab them from the end of the list.
                    var pointIndex = houdiniGeo.pointCount - spline.points.Count + i;
                    houdiniGeo.pointRefs.Add(pointIndex);
                    houdiniGeo.vertexCount++;
                }
                
                var nurbCurvePrimitive = new NURBCurvePrimitive();
                for (var i = 0; i < spline.points.Count; i++)
                {
                    // Knowing that we just added a certain number of vertices, we can calculate which ones they were.
                    var vertexIndex = houdiniGeo.vertexCount - spline.points.Count + i;
                    nurbCurvePrimitive.indices.Add(vertexIndex);
                }

                // NOTE: This does not support EVERY kind of spline that GEO files can handle, but it supports
                // Bézier curves which is the kind that's most useful to export from Unity.
                nurbCurvePrimitive.order = 4;
                nurbCurvePrimitive.endInterpolation = true;
                
                // I'm not so well-versed in NURBS so I'm winging it a little bit here based on a wikipedia article
                // and some reference splines that I cooked up in Maya. If I made a mistake feel free to fix it.
                // Here's my sources:
                // https://en.wikipedia.org/wiki/Non-uniform_rational_B-spline#:~:text=The%20knot%20vector%20is%20a,control%20points%20plus%20curve%20order).
                // Also go watch this, it's really good: https://www.youtube.com/watch?v=jvPPXbo87ds
                var vertexCount = spline.points.Count;
                var knotCount = 2 + (vertexCount - nurbCurvePrimitive.order) / (nurbCurvePrimitive.order - 1);
                for (var i = 0; i < knotCount; i++)
                {
                    int multiplicity;
                    if (i == 0 || i == knotCount - 1)
                        multiplicity = nurbCurvePrimitive.order;
                    else
                        multiplicity = nurbCurvePrimitive.order - 1;
                    
                    for (var j = 0; j < multiplicity; j++)
                    {
                        nurbCurvePrimitive.knots.Add(i);
                    }
                }
                
                houdiniGeo.nurbCurvePrimitives.Add(nurbCurvePrimitive);
                houdiniGeo.primCount++;
            }
        }

        private static bool GetAttributeTypeAndSize(Type valueType, out HoudiniGeoAttributeType type, out int tupleSize)
        {
            type = HoudiniGeoAttributeType.Invalid;
            tupleSize = 0;
            
            if (valueType == typeof(bool))
            {
                type = HoudiniGeoAttributeType.Integer;
                tupleSize = 1;
            }
            else if (valueType == typeof(float))
            {
                type = HoudiniGeoAttributeType.Float;
                tupleSize = 1;
            }
            else if (valueType == typeof(int))
            {
                type = HoudiniGeoAttributeType.Integer;
                tupleSize = 1;
            }
            else if (valueType == typeof(string))
            {
                type = HoudiniGeoAttributeType.String;
                tupleSize = 1;
            }
            if (valueType == typeof(Vector2))
            {
                type = HoudiniGeoAttributeType.Float;
                tupleSize = 2;
            }
            else if (valueType == typeof(Vector3))
            {
                type = HoudiniGeoAttributeType.Float;
                tupleSize = 3;
            }
            else if (valueType == typeof(Vector4))
            {
                type = HoudiniGeoAttributeType.Float;
                tupleSize = 4;
            }
            else if (valueType == typeof(Vector2Int))
            {
                type = HoudiniGeoAttributeType.Integer;
                tupleSize = 2;
            }
            else if (valueType == typeof(Vector3Int))
            {
                type = HoudiniGeoAttributeType.Integer;
                tupleSize = 3;
            }
            else if (valueType == typeof(Quaternion))
            {
                type = HoudiniGeoAttributeType.Float;
                tupleSize = 4;
            }
            else if (valueType == typeof(Color))
            {
                type = HoudiniGeoAttributeType.Float;
                tupleSize = 3;
            }

            return type != HoudiniGeoAttributeType.Invalid;
        }

        private static bool TryCreateAttribute(
            this HoudiniGeo houdiniGeo, string name, HoudiniGeoAttributeType type, int tupleSize,
            HoudiniGeoAttributeOwner owner, out HoudiniGeoAttribute attribute)
        {
            attribute = new HoudiniGeoAttribute {type = type, tupleSize = tupleSize};

            if (attribute == null)
                return false;

            attribute.name = name;
            attribute.owner = owner;
            
            houdiniGeo.attributes.Add(attribute);

            switch (owner)
            {
                // If we are adding an attribute of an element type that already has elements present, we need to make sure 
                // that they have default values.
                case HoudiniGeoAttributeOwner.Vertex:
                    attribute.AddDefaultValues(houdiniGeo.vertexCount, type, tupleSize);
                    break;
                case HoudiniGeoAttributeOwner.Point:
                    attribute.AddDefaultValues(houdiniGeo.pointCount, type, tupleSize);
                    break;
                case HoudiniGeoAttributeOwner.Primitive:
                    attribute.AddDefaultValues(houdiniGeo.primCount, type, tupleSize);
                    break;
            }

            return true;
        }

        private static bool TryCreateAttribute(
            this HoudiniGeo houdiniGeo, FieldInfo fieldInfo, HoudiniGeoAttributeOwner owner,
            out HoudiniGeoAttribute attribute)
        {
            attribute = null;

            var valueType = fieldInfo.FieldType;

            var isValid = GetAttributeTypeAndSize(valueType, out var type, out var tupleSize);
            if (!isValid)
                return false;

            return TryCreateAttribute(houdiniGeo, fieldInfo.Name, type, tupleSize, owner, out attribute);
        }

        private static bool TryGetOrCreateAttribute(this HoudiniGeo houdiniGeo,
            FieldInfo fieldInfo, HoudiniGeoAttributeOwner owner, out HoudiniGeoAttribute attribute)
        {
            var isValid = GetAttributeTypeAndSize(fieldInfo.FieldType, out var type, out var _);
            if (!isValid)
            {
                attribute = null;
                return false;
            }

            var existedAlready = houdiniGeo.TryGetAttribute(fieldInfo.Name, type, owner, out attribute);
            if (existedAlready)
                return true;

            return TryCreateAttribute(houdiniGeo, fieldInfo, owner, out attribute);
        }

        public static bool TryGetGroup(
            this HoudiniGeo houdiniGeo,
            string name, HoudiniGeoGroupType groupType, out HoudiniGeoGroup @group)
        {
            switch (groupType)
            {
                case HoudiniGeoGroupType.Points:
                    foreach (var pointGroup in houdiniGeo.PointGroups.Where(pointGroup => pointGroup.name == name))
                    {
                        group = pointGroup;
                        return true;
                    }
                    break;
                case HoudiniGeoGroupType.Primitives:
                    foreach (var primitiveGroup in houdiniGeo.PrimitiveGroups.Where(primitiveGroup => primitiveGroup.name == name))
                    {
                        group = primitiveGroup;
                        return true;
                    }
                    break;
                case HoudiniGeoGroupType.Edges:
                    foreach (var edgeGroup in houdiniGeo.EdgeGroups.Where(edgeGroup => edgeGroup.name == name))
                    {
                        group = edgeGroup;
                        return true;
                    }
                    break;
                case HoudiniGeoGroupType.Invalid:
                default:
                    throw new ArgumentOutOfRangeException(nameof(groupType), groupType, null);
            }

            group = null;
            return false;
        }

        private static HoudiniGeoGroupType GetGroupType(Type groupType)
        {
            if (groupType == typeof(PointGroup))
                return HoudiniGeoGroupType.Points;
            if (groupType == typeof(EdgeGroup))
                return HoudiniGeoGroupType.Edges;
            if (groupType == typeof(PrimitiveGroup))
                return HoudiniGeoGroupType.Primitives;
            return HoudiniGeoGroupType.Invalid;
        }
        
        public static bool TryGetGroup<GroupType>(
            this HoudiniGeo houdiniGeo, string name, out GroupType group)
            where GroupType : HoudiniGeoGroup
        {
            var groupType = GetGroupType(typeof(GroupType));
            var result = houdiniGeo.TryGetGroup(name, groupType, out var groupBase);
            if (!result)
            {
                group = null;
                return false;
            }

            group = (GroupType)groupBase;
            return true;
        }

        private static bool TryCreateGroup(
            this HoudiniGeo houdiniGeo, string name, HoudiniGeoGroupType type, out HoudiniGeoGroup group)
        {
            switch (type)
            {
                case HoudiniGeoGroupType.Points:
                    var pointGroup = new PointGroup(name);
                    group = pointGroup;
                    houdiniGeo.PointGroups.Add(pointGroup);
                    return true;
                case HoudiniGeoGroupType.Primitives:
                    var primitiveGroup = new PrimitiveGroup(name);
                    group = primitiveGroup;
                    houdiniGeo.PrimitiveGroups.Add(primitiveGroup);
                    return true;
                case HoudiniGeoGroupType.Edges:
                    var edgeGroup = new EdgeGroup(name);
                    group = edgeGroup;
                    houdiniGeo.EdgeGroups.Add(edgeGroup);
                    return true;
                case HoudiniGeoGroupType.Invalid:
                default:
                    group = null;
                    return false;
            }
        }
        
        private static bool TryCreateGroup<GroupType>(
            this HoudiniGeo houdiniGeo, string name, HoudiniGeoGroupType type, out GroupType group)
            where GroupType : HoudiniGeoGroup
        {
            var groupType = GetGroupType(typeof(GroupType));
            var result = houdiniGeo.TryCreateGroup(name, groupType, out var groupBase);
            if (!result)
            {
                group = null;
                return false;
            }

            group = (GroupType)groupBase;
            return true;
        }
        
        public static bool TryGetOrCreateGroup(this HoudiniGeo houdiniGeo,
            string name, HoudiniGeoGroupType type, out HoudiniGeoGroup group)
        {
            var existedAlready = houdiniGeo.TryGetGroup(name, type, out group);
            if (existedAlready)
                return true;

            return TryCreateGroup(houdiniGeo, name, type, out group);
        }
        
        public static bool TryGetOrCreateGroup<GroupType>(this HoudiniGeo houdiniGeo,
            string name, HoudiniGeoGroupType type, out GroupType group)
            where GroupType : HoudiniGeoGroup
        {
            var groupType = GetGroupType(typeof(GroupType));
            var result = houdiniGeo.TryGetOrCreateGroup(name, groupType, out var groupBase);
            if (!result)
            {
                group = null;
                return false;
            }

            group = (GroupType)groupBase;
            return true;
        }

        public static PointCollection<PointType> GetPoints<PointType>(
            this HoudiniGeo houdiniGeo, bool translateCoordinateSystems = true)
            where PointType : PointData
        {
            var points = new PointCollection<PointType>();
            var pointType = typeof(PointType);

            for (var i = 0; i < houdiniGeo.pointCount; i++)
            {
                var hasDefaultConstructor = typeof(PointType).GetConstructor(Type.EmptyTypes) != null;
                
                // If the point type has a default constructor we can use that to create an instance and call the field
                // initializers. If not, that's fine too but then we're gonna create an uninitialized instance.
                PointType point;
                if (hasDefaultConstructor)
                    point = (PointType)Activator.CreateInstance(typeof(PointType));
                else
                    point = (PointType)FormatterServices.GetUninitializedObject(typeof(PointType));

                foreach (var attribute in houdiniGeo.attributes)
                {
                    var field = pointType.GetField(attribute.name);

                    // The point doesn't necessarily need to support every attribute that exists in the file.
                    if (field == null)
                        continue;

                    var value = GetAttributeValue(field.FieldType, attribute, i);

                    if (value != null)
                    {
                        // If specified, automatically translate the position to Unity's format.
                        if (translateCoordinateSystems && attribute.name == PositionAttributeName)
                        {
                            value = Units.ToUnityPosition((Vector3)value);
                        }
                        
                        // If specified, automatically translate the direction to Unity's format.
                        else if (translateCoordinateSystems &&
                                 attribute.name is NormalAttributeName or UpAttributeName)
                        {
                            value = Units.ToUnityDirection((Vector3)value);
                        }

                        // If specified, automatically translate the rotation to Unity's format.
                        else if (translateCoordinateSystems && attribute.name == RotationAttributeName)
                        {
                            value = Units.ToUnityRotation((Quaternion)value);
                        }
                        
                        field.SetValue(point, value);
                    }
                }

                points.Add(point);
            }

            return points;
        }

        private static object GetAttributeValue(Type type, HoudiniGeoAttribute attribute, int index)
        {
            if (type == typeof(bool))
                return attribute.intValues[index] == 1;
            if (type == typeof(float))
                return attribute.floatValues[index];
            if (type == typeof(int))
                return attribute.intValues[index];
            if (type == typeof(string))
                return attribute.stringValues[index];
            if (type == typeof(Vector2))
                return new Vector2(attribute.floatValues[index * 2], attribute.floatValues[index * 2 + 1]);
            if (type == typeof(Vector3))
                return new Vector3(attribute.floatValues[index * 3], attribute.floatValues[index * 3 + 1], attribute.floatValues[index * 3 + 2]);
            if (type == typeof(Vector4))
                return new Vector4(attribute.floatValues[index * 4], attribute.floatValues[index * 4 + 1], attribute.floatValues[index * 4 + 2], attribute.floatValues[index * 4 + 3]);
            if (type == typeof(Vector2Int))
                return new Vector2Int(attribute.intValues[index * 2], attribute.intValues[index * 2 + 1]);
            if (type == typeof(Vector3Int))
                return new Vector3Int(attribute.intValues[index * 3], attribute.intValues[index * 3 + 1], attribute.intValues[index * 3 + 2]);
            if (type == typeof(Quaternion))
                return new Quaternion(attribute.floatValues[index * 4], attribute.floatValues[index * 4 + 1], attribute.floatValues[index * 4 + 2], attribute.floatValues[index * 4 + 3]);
            if (type == typeof(Color))
                return new Color(attribute.floatValues[index * 3], attribute.floatValues[index * 3 + 1], attribute.floatValues[index * 3 + 2]);
            
            Debug.LogWarning($"Tried to get value of unrecognized type '{type.Name}'");
            return null;
        }
    }
}
