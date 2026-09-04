using System;
using BenchmarkDotNet.Attributes;

namespace Tedd.FastNoise.Benchmark.Benchmarks
{
    [Config(typeof(TestConfig))]
    public class ImageFill
    {
        private const int Iterations = 100_000;
        private const int ImageSizeX= 1024;
        private const int ImageSizeY= 1024;


        private double[] _image;
        private Memory<double> _imageMemory;
        //private Vector<double> _imageVector;
        private OriginalGenerator _originalGenerator;
        private Generator _newGenerator;


        [GlobalSetup]
        public void GlobalSetup()
        {
        }

        [IterationSetup]
        public void IterationSetup()
        {
            _image = new double[ImageSizeX*ImageSizeY];
            _imageMemory = new Memory<double>(_image);
            //_imageVector = new Vector<double>(_imageMemory.Span);
            _originalGenerator = new OriginalGenerator(0,2);
            _newGenerator = new Generator(0,2);
        }

        [Benchmark()]
        public void OriginalGenerator()
        {
            for (var x = 0; x < ImageSizeX; x++)
            {
                for (var y = 0; y < ImageSizeY; y++)
                {
                    var p = x + y * ImageSizeX;
                    _image[p] = _originalGenerator.Perlin(x, y, 0);
                }
            }
        }

        [Benchmark()]
        public void NewGenerator()
        {
            _newGenerator.Fill(_imageMemory.Span, ImageSizeX, ImageSizeY);
        }
    }
}