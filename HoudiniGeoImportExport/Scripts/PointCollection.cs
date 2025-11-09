/**
 * Houdini Geo File Importer for Unity
 *
 * Copyright 2015 by Waldo Bronchart <wbronchart@gmail.com>
 * Exporter added in 2021 by Roy Theunissen <roy.theunissen@live.nl>
 * Licensed under GNU General Public License 3.0 or later. 
 * Some rights reserved. See COPYING, AUTHORS.
 */

using System.Collections;
using System.Collections.Generic;

namespace Houdini.GeoImportExport
{
    public class PointCollection<PointType> : IList<PointType>
        where PointType : PointData
    {
        private readonly List<PointType> _points = new();

        #region IList Delegation
        public IEnumerator<PointType> GetEnumerator() => _points.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => _points.GetEnumerator();

        public void Add(PointType item) => _points.Add(item);

        public void AddRange(IEnumerable<PointType> collection) => _points.AddRange(collection);

        public void Clear() => _points.Clear();

        public bool Contains(PointType item) => _points.Contains(item);

        public void CopyTo(PointType[] array, int arrayIndex) => _points.CopyTo(array, arrayIndex);

        public bool Remove(PointType item) => _points.Remove(item);

        public int Count => _points.Count;

        public bool IsReadOnly => false;

        public int IndexOf(PointType item) => _points.IndexOf(item);

        public void Insert(int index, PointType item) => _points.Insert(index, item);

        public void RemoveAt(int index) => _points.RemoveAt(index);

        public PointType this[int index]
        {
            get => _points[index];
            set => _points[index] = value;
        }
        #endregion IList Delegation
    }
}
