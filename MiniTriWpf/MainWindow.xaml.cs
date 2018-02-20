using System;
using SharpDX;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D;
using SharpDX.DXGI;
using SharpDX.Windows;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
using D3D11 = SharpDX.Direct3D11;
using SharpDX.Mathematics.Interop;

namespace MiniTriWpf
{
    /// <summary>
    /// Example of Direct3D 11 hosted inside a WPF application using SharpDX and WPFDXInterop
    /// eliminating airspace issues
    /// 
    /// By iejeecee 2018
    /// 
    /// SharpDX: https://github.com/sharpdx/SharpDX
    /// WPFDXInterop: https://github.com/Microsoft/WPFDXInterop
    /// </summary>
    public partial class MainWindow : Window
    {
        D3D11.Device device;
        D3D11.DeviceContext deviceContext;
        D3D11.RenderTargetView renderTargetView;
        TimeSpan lastRender = TimeSpan.Zero;

        public MainWindow()
        {
            InitializeComponent();

            InitializeDevice();

            ImageGrid.Loaded += Grid_Loaded;
            ImageGrid.SizeChanged += Grid_SizeChanged;

            Closing += MainWindow_Closing;
        }

        private void Grid_Loaded(object sender, RoutedEventArgs e)
        {            
            InteropImage.WindowOwner = (new System.Windows.Interop.WindowInteropHelper(this)).Handle;
            InteropImage.IsFrontBufferAvailableChanged += InteropImage_IsFrontBufferAvailableChanged;
            InteropImage.OnRender += OnRender;
                     
            CompositionTarget.Rendering += CompositionTarget_Rendering;           
        }

        private void Grid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            Size size = Utils.WpfSizeToPixels(ImageGrid);

            InteropImage.SetPixelSize((int)size.Width, (int)size.Height);
        }

        void InitializeSharedBackBuffer(IntPtr resourcePtr)
        {
            // convert native pointer to DXGI shared resource
            Resource resource = CppObject.FromPointer<Resource>(resourcePtr).QueryInterface<Resource>();

            // convert shared resource to D3D11 Texture
            D3D11.Texture2D sharedBackbuffer = device.OpenSharedResource<D3D11.Texture2D>(resource.SharedHandle);

            // release reference
            resource.Dispose();

            // use D3D11 Texture as render target
            D3D11.RenderTargetViewDescription desc = new D3D11.RenderTargetViewDescription();
            desc.Format = Format.B8G8R8A8_UNorm;
            desc.Dimension = D3D11.RenderTargetViewDimension.Texture2D;
            desc.Texture2D.MipSlice = 0;

            renderTargetView = new D3D11.RenderTargetView(device, sharedBackbuffer, desc);
            deviceContext.OutputMerger.SetRenderTargets(renderTargetView);

            // release reference
            sharedBackbuffer.Dispose();

            // setup viewport
            Size size = Utils.WpfSizeToPixels(ImageGrid);

            deviceContext.Rasterizer.SetViewport(new Viewport(0, 0, (int)size.Width, (int)size.Height, 0.0f, 1.0f));
        }

        void OnRender(IntPtr resourcePtr, bool isNewSurface)
        {
            if (isNewSurface)
            {
                // a new surface has been created (e.g. after a resize)
                InitializeSharedBackBuffer(resourcePtr);
            }

            deviceContext.ClearRenderTargetView(renderTargetView, new RawColor4(0,0,0,1));
            deviceContext.Draw(3, 0);
            deviceContext.Flush();       
        }

        void CompositionTarget_Rendering(object sender, EventArgs e)
        {
            RenderingEventArgs args = (RenderingEventArgs)e;

            // It's possible for Rendering to call back twice in the same frame 
            // so only render when we haven't already rendered in this frame.
            if (this.lastRender != args.RenderingTime)
            {
                InteropImage.RequestRender();
                this.lastRender = args.RenderingTime;
            }
        }


        private void InteropImage_IsFrontBufferAvailableChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.NewValue == false)
            {
                // force recreation of lost frontbuffer
                Size size = Utils.WpfSizeToPixels(ImageGrid);

                InteropImage.SetPixelSize((int)size.Width + 1, (int)size.Height + 1);
                InteropImage.SetPixelSize((int)size.Width, (int)size.Height);

                InteropImage.RequestRender();
            }
           
        }

        void InitializeDevice()
        {
            // Create Direct3D 11 Device without SwapChain
            device = new D3D11.Device(DriverType.Hardware, D3D11.DeviceCreationFlags.BgraSupport);

            deviceContext = device.ImmediateContext;
         
            // Compile Vertex and Pixel shaders
            var vertexShaderByteCode = ShaderBytecode.CompileFromFile("MiniTri.fx", "VS", "vs_4_0", ShaderFlags.None, EffectFlags.None);
            var vertexShader = new D3D11.VertexShader(device, vertexShaderByteCode);

            var pixelShaderByteCode = ShaderBytecode.CompileFromFile("MiniTri.fx", "PS", "ps_4_0", ShaderFlags.None, EffectFlags.None);
            var pixelShader = new D3D11.PixelShader(device, pixelShaderByteCode);

            // Layout from VertexShader input signature
            var layout = new D3D11.InputLayout(
                device,
                ShaderSignature.GetInputSignature(vertexShaderByteCode),
                new[]
                    {
                        new D3D11.InputElement("POSITION", 0, Format.R32G32B32A32_Float, 0, 0),
                        new D3D11.InputElement("COLOR", 0, Format.R32G32B32A32_Float, 16, 0)
                    });

            // Instantiate Vertex buiffer from vertex data
            var vertices = D3D11.Buffer.Create(device, D3D11.BindFlags.VertexBuffer, new[]
                                  {
                                      new Vector4(0.0f, 0.5f, 0.5f, 1.0f), new Vector4(1.0f, 0.0f, 0.0f, 1.0f),
                                      new Vector4(0.5f, -0.5f, 0.5f, 1.0f), new Vector4(0.0f, 1.0f, 0.0f, 1.0f),
                                      new Vector4(-0.5f, -0.5f, 0.5f, 1.0f), new Vector4(0.0f, 0.0f, 1.0f, 1.0f)
                                  });

            // Prepare All the stages
            deviceContext.InputAssembler.InputLayout = layout;
            deviceContext.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
            deviceContext.InputAssembler.SetVertexBuffers(0, new D3D11.VertexBufferBinding(vertices, 32, 0));

            deviceContext.VertexShader.Set(vertexShader);            
            deviceContext.PixelShader.Set(pixelShader);

        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // release resources
            CompositionTarget.Rendering -= CompositionTarget_Rendering;
            deviceContext.Dispose();
            device.Dispose();
        }
    }
}
