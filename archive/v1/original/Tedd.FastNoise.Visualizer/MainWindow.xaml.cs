using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Tedd.FastNoise.Visualizer.ViewModels;

namespace Tedd.FastNoise.Visualizer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private MainViewModel _mainViewModel;

        public MainWindow()
        {
            InitializeComponent();
            _mainViewModel = new MainViewModel();
            DataContext = _mainViewModel;
            this.SizeChanged += (sender, args) => UpdateModelSize();
            UpdateModelSize();
        }

        private void UpdateModelSize()
        {
            _mainViewModel.UpdateImageSize(
                (int) DimensionsTabs.TabControl.ActualWidth,
                (int) DimensionsTabs.TabControl.ActualHeight);
        }
    }
}
