﻿using OpenTK;
using SolidWorks.Interop.sldworks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media.Media3D;
using OpenTK.Graphics.OpenGL;
using System.Windows.Media;
using Xarial.XCad.SolidWorks.Documents.Services;
using Xarial.XCad.SolidWorks;
using Xarial.XCad.SolidWorks.Documents;
using Xarial.XCad.Documents;

namespace Xarial.XCad.Examples.Sw.XamlImporter
{
    public class XamlOverlayDocument : SwDocumentHandler
    {
        private IMathUtility m_MathUtils;
        private ISwModelView m_View;
        private ISwDocument3D m_Doc;

        private GLControl m_GLControl;

        private bool m_IsBufferInit;

        private int m_ColorBufferId;
        private int m_ElementBufferId;
        private int m_NormalBufferId;
        private int m_VertexBufferId;

        private int m_IndicesCount;

        private Model3DGroup m_Model3D;

        public Model3DGroup Model3D
        {
            get
            {
                return m_Model3D;
            }
            set
            {
                m_Model3D = value;
                UpdateVisibleBox();
                m_IsBufferInit = false;
                m_View.Update();
            }
        }

        protected override void OnInit(ISwApplication app, ISwDocument doc)
        {
            m_Doc = (ISwDocument3D)doc;
            m_View = (ISwModelView)doc.ModelViews.Active;

            m_MathUtils = app.Sw.IGetMathUtility();

            if (m_View != null)
            {
                m_View.RenderCustomGraphics += OnRenderCustomGraphics;

                m_GLControl = new GLControl();
                m_GLControl.Context.MakeCurrent(null);
            }
        }

        private bool OnRenderCustomGraphics(IXModelView sender, IXCustomGraphicsContext context)
        {
            if (m_Model3D != null)
            {
                if (!m_IsBufferInit)
                {
                    InitBuffer();
                    m_IsBufferInit = true;
                }

                RenderBuffer();
            }

            return true;
        }

        private void UpdateVisibleBox()
        {
            m_Doc.Model.Extension.RemoveVisibleBox();

            if (m_Model3D != null)
            {
                var curBox = m_Model3D.Bounds;

                var pt1 = m_MathUtils.CreatePoint(new double[] { curBox.X, curBox.Y, curBox.Z }) as MathPoint;
                var pt2 = m_MathUtils.CreatePoint(new double[] { curBox.X + curBox.SizeX, curBox.Y + curBox.SizeY, curBox.Z + curBox.SizeZ }) as MathPoint;

                m_Doc.Model.Extension.SetVisibleBox(pt1, pt2);
            }
        }

        private void InitBuffer()
        {
            var posList = new List<Vector3d>();
            var indList = new List<uint>();
            var normList = new List<Vector3d>();
            var colList = new List<uint>();

            int indicesOffset = 0;

            if (m_Model3D != null)
            {
                foreach (var geometryModel in m_Model3D.Children)
                {
                    var geometryModel3d = geometryModel as GeometryModel3D;
                    if (geometryModel3d != null)
                    {
                        var color = Colors.Black;
                        var materialGrp = geometryModel3d.Material as MaterialGroup;
                        var material = materialGrp?.Children?.FirstOrDefault() as DiffuseMaterial;
                        if (material != null)
                        {
                            if (material.Brush is SolidColorBrush)
                            {
                                color = (material.Brush as SolidColorBrush).Color;
                            }
                        }

                        var geom = geometryModel3d.Geometry as MeshGeometry3D;

                        if (geom != null)
                        {
                            indList.AddRange(geom.TriangleIndices.Select(i => (uint)(i + indicesOffset)));
                            indicesOffset += geom.Positions.Count;

                            foreach (var pos in geom.Positions)
                            {
                                posList.Add(new Vector3d(pos.X, pos.Y, pos.Z));
                                colList.Add(ColorToArgb(color));
                            }

                            foreach (var norm in geom.Normals)
                            {
                                normList.Add(new Vector3d(norm.X, norm.Y, norm.Z));
                            }
                        }
                        else
                        {
                            throw new NotSupportedException("Only MeshGeometry3D is supported");
                        }
                    }
                    else
                    {
                        throw new NotSupportedException("Only GeometryModel3D is supported");
                    }
                }

                GenAndFillBuffer(posList.ToArray(), BufferTarget.ArrayBuffer, out m_VertexBufferId);
                GenAndFillBuffer(colList.ToArray(), BufferTarget.ArrayBuffer, out m_ColorBufferId);
                GenAndFillBuffer(normList.ToArray(), BufferTarget.ArrayBuffer, out m_NormalBufferId);
                GenAndFillBuffer(indList.ToArray(), BufferTarget.ElementArrayBuffer, out m_ElementBufferId);

                m_IndicesCount = indList.Count;
            }
        }

        private void RenderBuffer()
        {
            GL.Disable(EnableCap.Lighting);

            GL.BindBuffer(BufferTarget.ArrayBuffer, m_NormalBufferId);
            GL.NormalPointer(NormalPointerType.Double, Vector3d.SizeInBytes, IntPtr.Zero);
            GL.EnableClientState(ArrayCap.NormalArray);

            GL.BindBuffer(BufferTarget.ArrayBuffer, m_VertexBufferId);
            GL.VertexPointer(3, VertexPointerType.Double, Vector3d.SizeInBytes, IntPtr.Zero);
            GL.EnableClientState(ArrayCap.VertexArray);

            GL.BindBuffer(BufferTarget.ArrayBuffer, m_ColorBufferId);
            GL.ColorPointer(4, ColorPointerType.UnsignedByte, sizeof(int), IntPtr.Zero);
            GL.EnableClientState(ArrayCap.ColorArray);

            GL.BindBuffer(BufferTarget.ElementArrayBuffer, m_ElementBufferId);

            GL.DrawElements(PrimitiveType.Triangles, m_IndicesCount, DrawElementsType.UnsignedInt, IntPtr.Zero);
        }

        private uint ColorToArgb(Color color)
            => (uint)color.A << 24 | (uint)color.B << 16 | (uint)color.G << 8 | (uint)color.R;

        private void GenAndFillBuffer<T>(T[] dataBuffer, BufferTarget target, out int bufferId)
            where T : struct
        {
            GL.GenBuffers(1, out bufferId);
            GL.BindBuffer(target, bufferId);
            var size = dataBuffer.Length * BlittableValueType.StrideOf(dataBuffer);
            GL.BufferData(target, new IntPtr(size), dataBuffer, BufferUsageHint.StaticDraw);

            int bufferSize;

            GL.GetBufferParameter(target, BufferParameterName.BufferSize, out bufferSize);

            if (size != bufferSize)
            {
                throw new Exception("Buffer size mismatch");
            }

            GL.BindBuffer(target, 0);
        }

        protected override void Dispose(bool disposing)
        {
            if (m_View != null)
            {
                m_View.RenderCustomGraphics -= OnRenderCustomGraphics;
            }

            m_GLControl.Dispose();
        }
    }
}
