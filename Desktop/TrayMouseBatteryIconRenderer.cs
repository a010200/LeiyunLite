using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
namespace RazerBatteryTray.Desktop
{
    internal static class TrayMouseBatteryIconRenderer
    {
        internal static readonly Color Amber = Color.FromArgb(245,179,1), Red = Color.FromArgb(229,72,77);
        private static readonly Color Outline=Color.FromArgb(234,242,251), Muted=Color.FromArgb(166,179,196), Inside=Color.FromArgb(21,33,43), White=Color.FromArgb(247,250,255);
        internal static Color FillColor(int percent, Color green) { return percent<=10?Red:percent<=25?Amber:green; }
        // Layout coordinates are physical pixels, independently tuned at the Shell sizes.
        private static RectangleF Bounds(int size, bool inner)
        {
            float w=size==16?9.8f:size==20?12.6f:size==24?15.4f:size*.642f;
            float top=size==16?1f:size==20?1.2f:size==24?1.4f:size*.0583f;
            float rim=size==16?.9f:size==20?1f:size==24?1.1f:size*.0458f;
            return new RectangleF((size-w)/2+(inner?rim:0),top+(inner?rim:0),w-(inner?2*rim:0),size-2*top-(inner?2*rim:0));
        }
        private static GraphicsPath MousePath(RectangleF b)
        {
            float x=b.X,y=b.Y,w=b.Width,h=b.Height,c=x+w/2;
            var p=new GraphicsPath();
            p.AddBezier(c,y,x+w*.83f,y,x+w*.98f,y+h*.13f,x+w,y+h*.34f);
            p.AddBezier(x+w,y+h*.34f,x+w*1.02f,y+h*.49f,x+w*1.02f,y+h*.67f,x+w,y+h*.78f);
            p.AddBezier(x+w,y+h*.78f,x+w,y+h*.94f,x+w*.81f,y+h,c,y+h);
            p.AddBezier(c,y+h,x+w*.19f,y+h,x,y+h*.94f,x,y+h*.78f);
            p.AddBezier(x,y+h*.78f,x-w*.02f,y+h*.67f,x-w*.02f,y+h*.49f,x,y+h*.34f);
            p.AddBezier(x,y+h*.34f,x+w*.02f,y+h*.13f,x+w*.17f,y,c,y);
            p.CloseFigure();return p;
        }
        internal static Bitmap Render(BatteryTrayVisual state,Color green)
        {
            int size=state.Size;
            if(size<16||size>256||state.Percent< -1||state.Percent>100)throw new ArgumentOutOfRangeException("state");
            // Eight samples per physical pixel at native tray sizes; bounded
            // work for larger fallback icons (4x through 64, 2x thereafter).
            int scale=size<=32?8:size<=64?4:2;
            using(var high=new Bitmap(size*scale,size*scale,PixelFormat.Format32bppPArgb))
            {
                using(var g=Graphics.FromImage(high))
                using(var outer=MousePath(Bounds(size,false)))
                using(var inner=MousePath(Bounds(size,true)))
                using(var ink=new SolidBrush(state.Percent<0?Muted:Outline))
                using(var dark=new SolidBrush(Inside))
                using(var fill=new SolidBrush(FillColor(state.Percent,green)))
                {
                    g.Clear(Color.Transparent);g.SmoothingMode=SmoothingMode.AntiAlias;
                    g.CompositingQuality=CompositingQuality.HighQuality;g.PixelOffsetMode=PixelOffsetMode.HighQuality;
                    g.ScaleTransform(scale,scale);g.FillPath(ink,outer);g.FillPath(dark,inner);
                    var saved=g.Save();g.SetClip(inner);
                    RectangleF b=Bounds(size,true);
                    if(state.Percent>0) {
                        float waterline=b.Bottom-b.Height*state.Percent/100f;
                        g.FillRectangle(fill,b.Left-1,waterline,b.Width+2,b.Bottom-waterline+1);
                    }
                    using(var seam=new Pen(Color.FromArgb(100,115,130),size*.038f)) {
                        seam.StartCap=seam.EndCap=LineCap.Round;g.DrawLine(seam,size/2f,b.Top,size/2f,size*.39f);
                    }
                    using(var wheel=new Pen(state.Percent<0?Muted:Outline,size==16?.8f:size*.05f)) {
                        wheel.StartCap=wheel.EndCap=LineCap.Round;g.DrawLine(wheel,size/2f,size*.20f,size/2f,size*.30f);
                    }
                    g.Restore(saved);
                    if(state.Charging)DrawChargingBadge(g,size,green);
                }
                return ResizePremultiplied(high,size,scale);
            }
        }
        internal static void PlugLayout(int size,out int cx,out int cy,out int diameter)
        {
            diameter=size<=18?6:size<=22?8:size<=24?9:Math.Max(8,(int)Math.Round(size*.31));
            cx=size-(diameter+1)/2;cy=(diameter+1)/2;
        }
        private static void DrawChargingBadge(Graphics g,int size,Color green)
        {
            int cx,cy,d;PlugLayout(size,out cx,out cy,out d);
            // A half-pixel inset keeps all four output corners truly transparent.
            float left=cx-d/2f-.4f,top=cy-d/2f+.6f,diameter=d-1f;
            using(var brush=new SolidBrush(green))g.FillEllipse(brush,left,top,diameter,diameter);
            float unit=diameter/6f,center=left+diameter/2;
            using(var pen=new Pen(White,unit*.75f)) {
                pen.StartCap=pen.EndCap=LineCap.Round;
                g.DrawLine(pen,center-unit,top+unit*1.5f,center-unit,top+unit*2.8f);
                g.DrawLine(pen,center+unit,top+unit*1.5f,center+unit,top+unit*2.8f);
                g.DrawLine(pen,center,top+unit*4.1f,center,top+unit*4.9f);
            }
            using(var path=new GraphicsPath())using(var brush=new SolidBrush(White)) {
                path.AddLine(center-unit*1.5f,top+unit*2.5f,center+unit*1.5f,top+unit*2.5f);
                path.AddBezier(center+unit*1.5f,top+unit*2.5f,center+unit*1.5f,top+unit*4.2f,center+unit*.8f,top+unit*4.4f,center,top+unit*4.4f);
                path.AddBezier(center,top+unit*4.4f,center-unit*.8f,top+unit*4.4f,center-unit*1.5f,top+unit*4.2f,center-unit*1.5f,top+unit*2.5f);
                path.CloseFigure();g.FillPath(brush,path);
            }
        }
        private static double Kernel(double x)
        {
            x=Math.Abs(x)*4/3;
            // Compact cubic B-spline, support 1.5 output pixels. Positive
            // weights avoid Lanczos ringing at transparent small-icon edges.
            if(x<1)return (4-6*x*x+3*x*x*x)/6;
            if(x<2)return Math.Pow(2-x,3)/6;
            return 0;
        }
        private sealed class Samples
        {
            internal readonly int Start;internal readonly double[] Weight;
            internal Samples(int output,int length,int scale) {
                double center=(output+.5)*scale-.5;
                Start=Math.Max(0,(int)Math.Ceiling(center-1.5*scale));
                int end=Math.Min(length-1,(int)Math.Floor(center+1.5*scale));
                Weight=new double[end-Start+1];double total=0;
                for(int i=0;i<Weight.Length;i++){Weight[i]=Kernel((Start+i-center)/scale);total+=Weight[i];}
                for(int i=0;i<Weight.Length;i++)Weight[i]/=total;
            }
        }
        // Filter BGRA in premultiplied space. Never interpolate transparent black
        // as straight RGB; unpremultiply only once when writing the final ARGB.
        private static Bitmap ResizePremultiplied(Bitmap high,int size,int scale)
        {
            int length=high.Width;var source=new byte[length*length*4];
            var data=high.LockBits(new Rectangle(0,0,length,length),ImageLockMode.ReadOnly,PixelFormat.Format32bppPArgb);
            try{for(int y=0;y<length;y++)Marshal.Copy(IntPtr.Add(data.Scan0,y*data.Stride),source,y*length*4,length*4);}
            finally{high.UnlockBits(data);}
            var samples=new Samples[size];for(int x=0;x<size;x++)samples[x]=new Samples(x,length,scale);
            var horizontal=new float[length*size*4];
            for(int y=0;y<length;y++)for(int x=0;x<size;x++) {
                var s=samples[x];int dest=(y*size+x)*4;
                for(int k=0;k<s.Weight.Length;k++) {
                    int src=(y*length+s.Start+k)*4;double w=s.Weight[k];
                    for(int c=0;c<4;c++)horizontal[dest+c]+=(float)(source[src+c]*w);
                }
            }
            var pixels=new int[size*size];
            for(int y=0;y<size;y++)for(int x=0;x<size;x++) {
                var s=samples[y];double b=0,g=0,r=0,a=0;
                for(int k=0;k<s.Weight.Length;k++) {
                    int src=((s.Start+k)*size+x)*4;double w=s.Weight[k];
                    b+=horizontal[src]*w;g+=horizontal[src+1]*w;r+=horizontal[src+2]*w;a+=horizontal[src+3]*w;
                }
                int alpha=Byte(a);if(alpha==0)continue;
                pixels[y*size+x]=(alpha<<24)|(Byte(r*255/Math.Max(a,1e-9))<<16)|(Byte(g*255/Math.Max(a,1e-9))<<8)|Byte(b*255/Math.Max(a,1e-9));
            }
            var result=new Bitmap(size,size,PixelFormat.Format32bppArgb);
            try {
                var output=result.LockBits(new Rectangle(0,0,size,size),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
                try{for(int y=0;y<size;y++)Marshal.Copy(pixels,y*size,IntPtr.Add(output.Scan0,y*output.Stride),size);}
                finally{result.UnlockBits(output);}return result;
            }catch{result.Dispose();throw;}
        }

        // .NET Framework Bitmap.GetHicon double-premultiplies translucent edges
        // on this runtime. Let Windows decode straight BGRA from a memory DIB.
        // The caller owns the returned handle and retains its Clone/DestroyIcon flow.
        internal static IntPtr CreateNativeIcon(Bitmap bitmap)
        {
            int size=bitmap.Width,maskStride=((size+31)/32)*4;
            byte[] dib;
            using(var stream=new System.IO.MemoryStream())
            using(var writer=new System.IO.BinaryWriter(stream)) {
                writer.Write(40);writer.Write(size);writer.Write(size*2);
                writer.Write((ushort)1);writer.Write((ushort)32);writer.Write(0);
                writer.Write(size*size*4);writer.Write(0);writer.Write(0);writer.Write(0);writer.Write(0);
                var row=new int[size];
                var data=bitmap.LockBits(new Rectangle(0,0,size,size),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
                try {
                    for(int y=size-1;y>=0;y--) {
                        Marshal.Copy(IntPtr.Add(data.Scan0,y*data.Stride),row,0,size);
                        for(int x=0;x<size;x++)writer.Write(row[x]);
                    }
                }finally{bitmap.UnlockBits(data);}
                writer.Write(new byte[maskStride*size]);writer.Flush();dib=stream.ToArray();
            }
            IntPtr handle=CreateIconFromResourceEx(dib,(uint)dib.Length,true,0x00030000,size,size,0);
            if(handle==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            return handle;
        }
        [DllImport("user32.dll",SetLastError=true)]
        private static extern IntPtr CreateIconFromResourceEx(byte[] bits,uint length,[MarshalAs(UnmanagedType.Bool)]bool icon,uint version,int width,int height,uint flags);
        private static int Byte(double value){return (int)Math.Max(0,Math.Min(255,Math.Round(value)));}
    }
}
