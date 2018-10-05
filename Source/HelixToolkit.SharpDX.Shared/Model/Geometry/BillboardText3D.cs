﻿/*
The MIT License (MIT)
Copyright (c) 2018 Helix Toolkit contributors
*/
using System.Collections.Generic;
using SharpDX;
using System;
using System.Linq;
using System.IO;
using System.Reflection;
using Cyotek.Drawing.BitmapFont;
#if CORE

#else
#if NETFX_CORE
using Media = Windows.UI.Xaml.Media;
#else
using Media = System.Windows.Media;
#endif
#endif

#if NETFX_CORE
namespace HelixToolkit.UWP
#else
namespace HelixToolkit.Wpf.SharpDX
#endif
{
    using Core;
    using System.Collections.ObjectModel;
    using System.Diagnostics;

    public class TextInfo
    {
        public string Text { get; set; }
        public Vector3 Origin { get; set; }

        public Color4 Foreground { set; get; } = Color.Black;

        public Color4 Background { set; get; } = Color.Transparent;

        public float ActualWidth { protected set; get; }

        public float AcutalHeight { protected set; get; }

        public float Scale { set; get; } = 1;
        /// <summary>
        /// Gets or sets the rotation angle in radians.
        /// </summary>
        /// <value>
        /// The angle in radians.
        /// </value>
        public float Angle { set; get; } = 0;

        public TextInfo()
        {
        }

        public TextInfo(string text, Vector3 origin)
        {
            Text = text;
            Origin = origin;
        }

        public virtual void UpdateTextInfo(float actualWidth, float actualHeight)
        {
            ActualWidth = actualWidth;
            AcutalHeight = actualHeight;
            BoundSphere = new BoundingSphere(Origin, Math.Max(actualWidth, actualHeight) / 2);
        }

        public BoundingSphere BoundSphere { get; private set; }
    }

#if !NETFX_CORE
    [Serializable]
#endif
    public class BillboardText3D : BillboardBase
    {
        private readonly static BitmapFont bmpFont;

        public static Stream TextureStatic { get; private set; }
        const float textureScale = 0.66f;
        const string FontName = "arial";
        static BillboardText3D()
        {
#if CORE
            var assembly = typeof(BillboardText3D).GetTypeInfo().Assembly;
            Stream fontInfo = assembly.GetManifestResourceStream($"HelixToolkit.SharpDX.Core.Resources.{FontName}.fnt");
            bmpFont = new BitmapFont();
            bmpFont.Load(fontInfo);
            Stream font = assembly.GetManifestResourceStream($"HelixToolkit.SharpDX.Core.Resources.{FontName}.dds");
            TextureStatic = font;
#else
#if !NETFX_CORE
            var assembly = Assembly.GetExecutingAssembly();

            //Read the texture description           
            var texDescriptionStream = assembly.GetManifestResourceStream($"HelixToolkit.Wpf.SharpDX.Textures.{FontName}.fnt");

            bmpFont = new BitmapFont();
            bmpFont.Load(texDescriptionStream);// BitmapFontLoader.LoadFontFromFile(texDescriptionFilePath);
            texDescriptionStream.Dispose();
            //Read the texture          
            var texImageStream = assembly.GetManifestResourceStream($"HelixToolkit.Wpf.SharpDX.Textures.{FontName}.dds");
            TextureStatic = MemoryStream.Synchronized(texImageStream);
#else
            var packageFolder = Path.Combine(Windows.ApplicationModel.Package.Current.InstalledLocation.Path, "HelixToolkit.UWP");
            var sampleFile = global::SharpDX.IO.NativeFile.ReadAllBytes(packageFolder + $"\\Resources\\{FontName}.fnt");
            bmpFont = new BitmapFont();
            var fileStream = new MemoryStream(sampleFile);
            bmpFont.Load(fileStream);

            var texFile = global::SharpDX.IO.NativeFile.ReadAllBytes(packageFolder + $"\\Resources\\{FontName}.dds");
            TextureStatic = new MemoryStream(texFile);         
#endif
#endif
        }

        public override BillboardType Type
        {
            get
            {
                return BillboardType.MultipleText;
            }
        }

        public virtual BitmapFont BitmapFont
        {
            get
            {
                return bmpFont;
            }
        }

        private ObservableCollection<TextInfo> textInfo = new ObservableCollection<TextInfo>();
        public ObservableCollection<TextInfo> TextInfo
        {
            set
            {
                var old = textInfo;
                if(Set(ref textInfo, value))
                {
                    old.CollectionChanged -= CollectionChanged;
                    IsInitialized = false;
                    if (value != null)
                    {
                        value.CollectionChanged += CollectionChanged;
                    }
                }
            }
            get { return textInfo; }
        }

        public BillboardText3D()
        {
            textInfo.CollectionChanged += CollectionChanged;
        }

        protected override void OnAssignTo(Geometry3D target)
        {
            base.OnAssignTo(target);
            if(target is BillboardText3D billboard)
            {
                billboard.TextInfo = this.TextInfo;
            }
        }

        private void CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            IsInitialized = false;
        }

        protected override void OnDrawTexture(IDeviceResources deviceResources)
        {
            Texture = TextureStatic;
            Width = 0;
            Height = 0;
            // http://www.cyotek.com/blog/angelcode-bitmap-font-parsing-using-csharp
            var tempList = new List<BillboardVertex>(100);

            foreach (var textInfo in TextInfo)
            {
                int tempPrevCount = tempList.Count;
                int x = 0;
                int y = 0;
                var w = BitmapFont.TextureSize.Width;
                var h = BitmapFont.TextureSize.Height;
                char previousCharacter;

                previousCharacter = ' ';
                var normalizedText = textInfo.Text;
                var rect = new RectangleF(textInfo.Origin.X, textInfo.Origin.Y, 0, 0);
                foreach (char character in normalizedText)
                {
                    switch (character)
                    {
                        case '\n':
                            x = 0;
                            y -= BitmapFont.LineHeight;
                            break;
                        default:
                            Character data = BitmapFont[character];
                            int kerning = BitmapFont.GetKerning(previousCharacter, character);
                            tempList.Add(DrawCharacter(data, new Vector3(x + data.Offset.X, y - data.Offset.Y, 0), w, h, kerning, textInfo));

                            x += data.XAdvance + kerning;
                            break;
                    }
                    previousCharacter = character;
                    if (tempList.Count > 0)
                    {
                        rect.Width = Math.Max(rect.Width, x * textInfo.Scale * textureScale);
                        rect.Height = Math.Max(rect.Height, Math.Abs(tempList.Last().OffBR.Y));
                    }
                }
                var transform = textInfo.Angle != 0 ? Matrix3x2.Rotation(textInfo.Angle) : Matrix3x2.Identity;
                var halfW = rect.Width / 2;
                var halfH = rect.Height / 2;
                //Add backbround vertex first. This also used for hit test
                BillboardVertices.Add(new BillboardVertex()
                {
                    Position = textInfo.Origin.ToVector4(),
                    Background = textInfo.Background,
                    TexTL = Vector2.Zero,
                    TexBR = Vector2.Zero,
                    OffTL = Matrix3x2.TransformPoint(transform, new Vector2(-halfW, halfH)),
                    OffBR = Matrix3x2.TransformPoint(transform, new Vector2(halfW, -halfH)),
                    OffTR = Matrix3x2.TransformPoint(transform, new Vector2(-halfW, -halfH)),
                    OffBL = Matrix3x2.TransformPoint(transform, new Vector2(halfW, halfH)),
                });

                textInfo.UpdateTextInfo(rect.Width, rect.Height);

                for(int k = tempPrevCount; k < tempList.Count; ++k)
                {
                    var v = tempList[k];
                    v.OffTL = Matrix3x2.TransformPoint(transform, v.OffTL + new Vector2(-halfW, halfH));
                    v.OffBR = Matrix3x2.TransformPoint(transform, v.OffBR + new Vector2(-halfW, halfH));
                    v.OffTR = Matrix3x2.TransformPoint(transform, v.OffTR + new Vector2(-halfW, halfH));
                    v.OffBL = Matrix3x2.TransformPoint(transform, v.OffBL + new Vector2(-halfW, halfH));
                    tempList[k] = v;
                }
                Width += rect.Width;
                Height += rect.Height;
            }

            foreach(var v in tempList)
            {
                BillboardVertices.Add(v);
            }
        }

        public override void UpdateBounds()
        {
            if (TextInfo.Count == 0)
            {
                Bound = new BoundingBox();
                BoundingSphere = new BoundingSphere();
            }
            else
            {
                var sphere = TextInfo[0].BoundSphere;
                var bound = BoundingBox.FromSphere(sphere);
                foreach(var info in TextInfo)
                {
                    sphere = BoundingSphere.Merge(sphere, info.BoundSphere);
                    bound = BoundingBox.Merge(bound, BoundingBox.FromSphere(info.BoundSphere));
                }
                BoundingSphere = sphere;
                Bound = bound;
            }
        }

        private BillboardVertex DrawCharacter(Character character, Vector3 origin, float w, float h, float kerning, TextInfo info)
        {
            var cw = character.Bounds.Width;
            var ch = character.Bounds.Height;
            var cu = character.Bounds.Left;
            var cv = character.Bounds.Top;
            var tl = new Vector2(origin.X + kerning, origin.Y );
            var br = new Vector2(origin.X + cw + kerning, origin.Y - ch);
            var offTL = tl * info.Scale * textureScale;
            var offBR = br * info.Scale * textureScale;
            var offTR = new Vector2(offBR.X, offTL.Y);
            var offBL = new Vector2(offTL.X, offBR.Y);
            var uv_tl = new Vector2(cu / w, cv / h);
            var uv_br = new Vector2((cu + cw) / w, (cv + ch) / h);
            
            return new BillboardVertex()
            {
                Position = info.Origin.ToVector4(),
                Foreground = info.Foreground,
                Background = Color.Transparent,
                TexTL = uv_tl,
                TexBR = uv_br,
                OffTL = offTL,
                OffBL = offBL,
                OffBR = offBR,
                OffTR = offTR
            };
        }

        public override bool HitTest(RenderContext context, Matrix modelMatrix, ref Ray rayWS, ref List<HitTestResult> hits, 
            object originalSource, bool fixedSize)
        {
            if (!IsInitialized || context == null || Width == 0 || Height == 0 || (!fixedSize && !BoundingSphere.TransformBoundingSphere(modelMatrix).Intersects(ref rayWS)))
            {
                return false;
            }

            return fixedSize ? HitTestFixedSize(context, ref modelMatrix, ref rayWS, ref hits, originalSource, textInfo.Count)
                : HitTestNonFixedSize(context, ref modelMatrix, ref rayWS, ref hits, originalSource, textInfo.Count);
        }

        /// <summary>
        /// Hits the size of the test fixed.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="modelMatrix">The model matrix.</param>
        /// <param name="rayWS">The ray ws.</param>
        /// <param name="hits">The hits.</param>
        /// <param name="originalSource">The original source.</param>
        /// <param name="count">The count of vertices in <see cref="BillboardVertices"/>.</param>
        /// <returns></returns>
        protected bool HitTestFixedSize(RenderContext context, ref Matrix modelMatrix,
            ref Ray rayWS, ref List<HitTestResult> hits,
            object originalSource, int count)
        {
            var h = false;
            var result = new BillboardHitResult
            {
                Distance = double.MaxValue
            };
            var visualToScreen = context.ScreenViewProjectionMatrix;
            var screenPoint3D = Vector3.TransformCoordinate(rayWS.Position, visualToScreen);
            var screenPoint = new Vector2(screenPoint3D.X, screenPoint3D.Y);
            for (int i = 0; i < count; ++i)
            {
                var vert = BillboardVertices[i];
                var pos = vert.Position.ToVector3();
                var c = Vector3.TransformCoordinate(pos, modelMatrix);
                var dir = c - rayWS.Position;
                if (Vector3.Dot(dir, rayWS.Direction) < 0)
                {
                    continue;
                }
                var quad = GetScreenQuad(ref c, ref vert.OffTL, ref vert.OffTR, ref vert.OffBL, ref vert.OffBR, ref visualToScreen);
                if (quad.IsPointInQuad2D(ref screenPoint))
                {
                    var dist = (rayWS.Position - c).Length();
                    if(dist > result.Distance)
                    {
                        continue;
                    }
                    h = true;
                    result.ModelHit = originalSource;
                    result.IsValid = true;
                    result.PointHit = c;
                    result.Distance = dist;
                    result.Geometry = this;
                    result.TextInfo = TextInfo[i];
                    result.TextInfoIndex = i;
                    Debug.WriteLine(string.Format("Hit; HitPoint:{0}; Text={1}", result.PointHit, result.TextInfo.Text));
                }
            }
            if (h)
            {
                hits.Add(result);
            }
            return h;
        }
        /// <summary>
        /// Hits the size of the test non fixed.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="modelMatrix">The model matrix.</param>
        /// <param name="rayWS">The ray ws.</param>
        /// <param name="hits">The hits.</param>
        /// <param name="originalSource">The original source.</param>
        /// <param name="count">The count of vertices in <see cref="BillboardVertices"/>.</param>
        /// <returns></returns>
        protected bool HitTestNonFixedSize(RenderContext context, ref Matrix modelMatrix,
            ref Ray rayWS, ref List<HitTestResult> hits,
            object originalSource, int count)
        {
            var h = false;
            var result = new BillboardHitResult
            {
                Distance = double.MaxValue
            };
            var viewMatrix = context.ViewMatrix;
            var viewMatrixInv = viewMatrix.PsudoInvert();
            for (int i = 0; i < count; ++i)
            {
                var vert = BillboardVertices[i];
                var pos = vert.Position.ToVector3();
                var c = Vector3.TransformCoordinate(pos, modelMatrix);
                var dir = c - rayWS.Position;
                if (Vector3.Dot(dir, rayWS.Direction) < 0)
                {
                    continue;
                }
                var quad = GetHitTestQuad(ref c, ref vert.OffTL, ref vert.OffTR, ref vert.OffBL, ref vert.OffBR, ref viewMatrix, ref viewMatrixInv);
                if (Collision.RayIntersectsTriangle(ref rayWS, ref quad.TL, ref quad.TR, ref quad.BR, out Vector3 hitPoint)
                    || Collision.RayIntersectsTriangle(ref rayWS, ref quad.TL, ref quad.BR, ref quad.BL, out hitPoint))
                {
                    var dist = (rayWS.Position - hitPoint).Length();
                    if (dist > result.Distance)
                    {
                        continue;
                    }
                    h = true;
                    result.ModelHit = originalSource;
                    result.IsValid = true;
                    result.PointHit = hitPoint;
                    result.Distance = dist;
                    result.Geometry = this;
                    result.TextInfo = TextInfo[i];
                    result.TextInfoIndex = i;
                    Debug.WriteLine(string.Format("Hit; HitPoint:{0}; Text={1}", result.PointHit, result.TextInfo.Text));
                }
            }
            if (h)
            {
                hits.Add(result);
            }
            return h;
        }
    }

}
