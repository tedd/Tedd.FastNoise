using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Tedd.FastNoise.Visualizer.Annotations;

namespace Tedd.FastNoise.Visualizer.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double Zoom { get; set; } = 200;

        public EditBitmap Image1D { get; set; }
        public EditBitmap Image2D { get; set; }
        public EditBitmap Image3D { get; set; }

        public Generator Generator1 { get; set; }
        public Generator Generator2 { get; set; }
        public Generator Generator3 { get; set; }

        public MainViewModel()
        {
            Generator1 = new Generator(0, 1);
            Generator2 = new Generator(0, 2);
            Generator3 = new Generator(0, 3);

            PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == nameof(Zoom)
                    || args.PropertyName == nameof(X)
                    || args.PropertyName == nameof(Y)
                    || args.PropertyName == nameof(Z))
                    Redraw();
            };
        }

        public void UpdateImageSize(int x, int y)
        {
            if (x == 0 || y == 0)
                return;
            Image1D?.Dispose();
            Image1D = new EditBitmap(x, y, PixelFormats.Bgr24);
            Image2D?.Dispose();
            Image2D = new EditBitmap(x, y, PixelFormats.Bgr24);
            Image3D?.Dispose();
            Image3D = new EditBitmap(x, y, PixelFormats.Bgr24);

            Redraw();
        }

        private void Redraw()
        {
            Redraw(Image1D, Generator1);
            //Redraw(Image2D, Generator2);
            //Redraw(Image3D, Generator3);
        }

        private unsafe void Redraw(EditBitmap image, Generator g)
        {
            var w = image.Width;
            var h = image.Height;

            for (var x = 0; x < w; x++)
            //Parallel.For(0, w, new ParallelOptions { MaxDegreeOfParallelism = 32 }, (x) =>
             {
                 for (var y = 0; y < h; y++)
                 {
                     var p = y * w + x;
                     var v = g.Perlin((double)x / (double)w * ((double)Zoom / 10D), (double)y / (double)h * ((double)Zoom / 10D), 0);
                     var c = (byte)(255D * v);
                     image.ImageBytePtr[p * 3 + 0] = c;
                     image.ImageBytePtr[p * 3 + 1] = c;
                     image.ImageBytePtr[p * 3 + 2] = c;
                 }
             }
                //);
            image.Invalidate();
        }

        #region INotifyPropertyChanged
        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion
    }
}
