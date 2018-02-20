using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MiniTriWpf
{
    class Utils
    {
        public static Size WpfSizeToPixels(FrameworkElement element)
        {
            var source = PresentationSource.FromVisual(element);
            Matrix transformToDevice = source.CompositionTarget.TransformToDevice;

            return (Size)transformToDevice.Transform(new Vector(element.ActualWidth, element.ActualHeight));
        }
    }
}
