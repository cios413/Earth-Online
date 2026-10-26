using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace YoloSegmentation
{
    /// <summary>
    /// Draws polylines as quads inside a UI Canvas (Built-in Render Pipeline compatible).
    /// Points are in the RectTransform's local space. Assign through SetPolylines.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class UILineRenderer : MaskableGraphic
    {
        [SerializeField] float thickness = 4f;

        readonly List<Vector2[]> polylines = new List<Vector2[]>();

        public float Thickness
        {
            get => thickness;
            set
            {
                thickness = Mathf.Max(0f, value);
                SetVerticesDirty();
            }
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            raycastTarget = false;
        }

        public void SetPolylines(IReadOnlyList<Vector2[]> lines, float lineThickness, Color lineColor)
        {
            thickness = Mathf.Max(0f, lineThickness);
            color = lineColor;
            polylines.Clear();
            foreach (var line in lines)
                polylines.Add(line);

            SetVerticesDirty();
        }

        public void Clear()
        {
            polylines.Clear();
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (polylines.Count == 0 || thickness <= 0f)
                return;

            var half = thickness * 0.5f;

            foreach (var line in polylines)
            {
                for (var i = 0; i + 1 < line.Length; i++)
                {
                    var a = line[i];
                    var b = line[i + 1];
                    var direction = b - a;
                    var length = direction.magnitude;
                    if (length < 0.0001f)
                        continue;

                    var normal = new Vector2(-direction.y, direction.x) / length * half;
                    AddQuad(vh, a - normal, a + normal, b + normal, b - normal);
                }
            }
        }

        void AddQuad(VertexHelper vh, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3)
        {
            var start = vh.currentVertCount;

            var vertex = UIVertex.simpleVert;
            vertex.color = color;

            vertex.position = p0;
            vh.AddVert(vertex);
            vertex.position = p1;
            vh.AddVert(vertex);
            vertex.position = p2;
            vh.AddVert(vertex);
            vertex.position = p3;
            vh.AddVert(vertex);

            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
