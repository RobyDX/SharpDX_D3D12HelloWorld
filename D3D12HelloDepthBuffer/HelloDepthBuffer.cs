using SharpDX.DXGI;
using System.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace D3D12HelloDepthBuffer
{
    using SharpDX;
    using SharpDX.Direct3D12;
    using SharpDX.Windows;

    public class HelloDepthBuffer
         : IDisposable
    {
        /// <summary>
        /// Initialise pipeline and assets
        /// </summary>
        /// <param name="form">The form</param>
        public void Initialize(RenderForm form)
        {
            screenForm = form;
            LoadPipeline(form);
            LoadAssets();
        }

        private void LoadPipeline(RenderForm form)
        {
            int width = form.ClientSize.Width;
            int height = form.ClientSize.Height;

            viewport.Width = width;
            viewport.Height = height;
            viewport.MaxDepth = 1.0f;

            scissorRect.Right = width;
            scissorRect.Bottom = height;

#if DEBUG
            // Enable the D3D12 debug layer.
            {
                DebugInterface.Get().EnableDebugLayer();
            }
#endif
            device = new Device(null, SharpDX.Direct3D.FeatureLevel.Level_11_0);
            using (var factory = new Factory4())
            {
                // Describe and create the command queue.
                CommandQueueDescription queueDesc = new CommandQueueDescription(CommandListType.Direct);
                commandQueue = device.CreateCommandQueue(queueDesc);


                // Describe and create the swap chain.
                SwapChainDescription swapChainDesc = new SwapChainDescription()
                {
                    BufferCount = FrameCount,
                    ModeDescription = new ModeDescription(width, height, new Rational(60, 1), Format.R8G8B8A8_UNorm),
                    Usage = Usage.RenderTargetOutput,
                    SwapEffect = SwapEffect.FlipDiscard,
                    OutputHandle = form.Handle,
                    //Flags = SwapChainFlags.None,
                    SampleDescription = new SampleDescription(1, 0),
                    IsWindowed = true
                };

                SwapChain tempSwapChain = new SwapChain(factory, commandQueue, swapChainDesc);
                swapChain = tempSwapChain.QueryInterface<SwapChain3>();
                tempSwapChain.Dispose();
                frameIndex = swapChain.CurrentBackBufferIndex;
            }

            // Create descriptor heaps.
            // Describe and create a render target view (RTV) descriptor heap.
            DescriptorHeapDescription rtvHeapDesc = new DescriptorHeapDescription()
            {
                DescriptorCount = FrameCount,
                Flags = DescriptorHeapFlags.None,
                Type = DescriptorHeapType.RenderTargetView
            };

            renderTargetViewHeap = device.CreateDescriptorHeap(rtvHeapDesc);

            rtvDescriptorSize = device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);

            // Create frame resources.
            CpuDescriptorHandle rtvHandle = renderTargetViewHeap.CPUDescriptorHandleForHeapStart;
            for (int n = 0; n < FrameCount; n++)
            {
                renderTargets[n] = swapChain.GetBackBuffer<Resource>(n);
                device.CreateRenderTargetView(renderTargets[n], null, rtvHandle);
                rtvHandle += rtvDescriptorSize;
            }


            //create depth buffer;
            DescriptorHeapDescription dsvHeapDesc = new DescriptorHeapDescription()
            {
                DescriptorCount = FrameCount,
                Flags = DescriptorHeapFlags.None,
                Type = DescriptorHeapType.DepthStencilView
            };
            depthStencilViewHeap = device.CreateDescriptorHeap(dsvHeapDesc);
            CpuDescriptorHandle dsvHandle = depthStencilViewHeap.CPUDescriptorHandleForHeapStart;

            ClearValue depthOptimizedClearValue = new ClearValue()
            {
                Format = Format.D32_Float,
                DepthStencil = new DepthStencilValue() { Depth = 1.0F, Stencil = 0 },
            };

            depthTarget = device.CreateCommittedResource(
                new HeapProperties(HeapType.Default),
                HeapFlags.None,
                new ResourceDescription(ResourceDimension.Texture2D, 0, width, height, 1, 0, Format.D32_Float, 1, 0, TextureLayout.Unknown, ResourceFlags.AllowDepthStencil),
                ResourceStates.DepthWrite, depthOptimizedClearValue);

            var depthView = new DepthStencilViewDescription()
            {
                Format = Format.D32_Float,
                Dimension = DepthStencilViewDimension.Texture2D,
                Flags = DepthStencilViewFlags.None,
            };

            //bind depth buffer
            device.CreateDepthStencilView(depthTarget, null, dsvHandle);

            commandAllocator = device.CreateCommandAllocator(CommandListType.Direct);
            bundleAllocator = device.CreateCommandAllocator(CommandListType.Bundle);
        }

        private void LoadAssets()
        {

            DescriptorRange[] ranges = new DescriptorRange[] { new DescriptorRange() { RangeType = DescriptorRangeType.ConstantBufferView, BaseShaderRegister = 0, DescriptorCount = 1 } };
            RootParameter parameter = new RootParameter(ShaderVisibility.Vertex, ranges);

            // Create a root signature.
            RootSignatureDescription rootSignatureDesc = new RootSignatureDescription(RootSignatureFlags.AllowInputAssemblerInputLayout, new RootParameter[] { parameter });
            rootSignature = device.CreateRootSignature(rootSignatureDesc.Serialize());

            // Create the pipeline state, which includes compiling and loading shaders.

#if DEBUG
            var vertexShader = new ShaderBytecode(SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile("shaders.hlsl", "VSMain", "vs_5_0", SharpDX.D3DCompiler.ShaderFlags.Debug));
#else
            var vertexShader = new ShaderBytecode(SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile("shaders.hlsl", "VSMain", "vs_5_0"));
#endif

#if DEBUG
            var pixelShader = new ShaderBytecode(SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile("shaders.hlsl", "PSMain", "ps_5_0", SharpDX.D3DCompiler.ShaderFlags.Debug));
#else
            var pixelShader = new ShaderBytecode(SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile("shaders.hlsl", "PSMain", "ps_5_0"));
#endif

            // Define the vertex input layout.
            InputElement[] inputElementDescs = new InputElement[]
            {
                    new InputElement("POSITION",0,Format.R32G32B32_Float,0,0),
                    new InputElement("COLOR",0,Format.R32G32B32A32_Float,12,0)
            };

            // Describe and create the graphics pipeline state object (PSO).
            GraphicsPipelineStateDescription psoDesc = new GraphicsPipelineStateDescription()
            {
                InputLayout = new InputLayoutDescription(inputElementDescs),
                RootSignature = rootSignature,
                VertexShader = vertexShader,
                PixelShader = pixelShader,
                RasterizerState = RasterizerStateDescription.Default(),
                BlendState = BlendStateDescription.Default(),
                DepthStencilFormat = SharpDX.DXGI.Format.D32_Float,
                DepthStencilState = DepthStencilStateDescription.Default(),
                SampleMask = int.MaxValue,
                PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
                RenderTargetCount = 1,
                Flags = PipelineStateFlags.None,
                SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0),
                StreamOutput = new StreamOutputDescription()
            };
            psoDesc.RenderTargetFormats[0] = SharpDX.DXGI.Format.R8G8B8A8_UNorm;

            pipelineState = device.CreateGraphicsPipelineState(psoDesc);

            // Create the command list.
            commandList = device.CreateCommandList(CommandListType.Direct, commandAllocator, pipelineState);

            // Command lists are created in the recording state, but there is nothing
            // to record yet. The main loop expects it to be closed, so close it now.
            commandList.Close();

            // Create the vertex buffer.
            float aspectRatio = viewport.Width / viewport.Height;

            // Define the geometry for a cube.

            Vertex[] vertices = new[]
            {
                ////TOP
                new Vertex(new Vector3(-5,5,5),new Vector4(0,1,0,0)),
                new Vertex(new Vector3(5,5,5),new Vector4(0,1,0,0)),
                new Vertex(new Vector3(5,5,-5),new Vector4(0,1,0,0)),
                new Vertex(new Vector3(-5,5,-5),new Vector4(0,1,0,0)),
                //BOTTOM
                new Vertex(new Vector3(-5,-5,5),new Vector4(1,0,1,1)),
                new Vertex(new Vector3(5,-5,5),new Vector4(1,0,1,1)),
                new Vertex(new Vector3(5,-5,-5),new Vector4(1,0,1,1)),
                new Vertex(new Vector3(-5,-5,-5),new Vector4(1,0,1,1)),
                //LEFT
                new Vertex(new Vector3(-5,-5,5),new Vector4(1,0,0,1)),
                new Vertex(new Vector3(-5,5,5),new Vector4(1,0,0,1)),
                new Vertex(new Vector3(-5,5,-5),new Vector4(1,0,0,1)),
                new Vertex(new Vector3(-5,-5,-5),new Vector4(1,0,0,1)),
                //RIGHT
                new Vertex(new Vector3(5,-5,5),new Vector4(1,1,0,1)),
                new Vertex(new Vector3(5,5,5),new Vector4(1,1,0,1)),
                new Vertex(new Vector3(5,5,-5),new Vector4(1,1,0,1)),
                new Vertex(new Vector3(5,-5,-5),new Vector4(1,1,0,1)),
                //FRONT
                new Vertex(new Vector3(-5,5,5),new Vector4(0,1,1,1)),
                new Vertex(new Vector3(5,5,5),new Vector4(0,1,1,1)),
                new Vertex(new Vector3(5,-5,5),new Vector4(0,1,1,1)),
                new Vertex(new Vector3(-5,-5,5),new Vector4(0,1,1,1)),
                //BACK
                new Vertex(new Vector3(-5,5,-5),new Vector4(0,0,1,1)),
                new Vertex(new Vector3(5,5,-5),new Vector4(0,0,1,1)),
                new Vertex(new Vector3(5,-5,-5),new Vector4(0,0,1,1)),
                new Vertex(new Vector3(-5,-5,-5),new Vector4(0,0,1,1))
            };

            int vertexBufferSize = Utilities.SizeOf(vertices);


            // Note: using upload heaps to transfer static data like vert buffers is not 
            // recommended. Every time the GPU needs it, the upload heap will be marshalled 
            // over. Please read up on Default Heap usage. An upload heap is used here for 
            // code simplicity and because there are very few verts to actually transfer.
            vertexBuffer = device.CreateCommittedResource(new HeapProperties(HeapType.Upload), HeapFlags.None, ResourceDescription.Buffer(vertexBufferSize), ResourceStates.GenericRead);

            // Copy the triangle data to the vertex buffer.
            IntPtr pVertexDataBegin = vertexBuffer.Map(0);
            Utilities.Write(pVertexDataBegin, vertices, 0, vertices.Length);
            vertexBuffer.Unmap(0);

            // Initialize the vertex buffer view.
            vertexBufferView = new VertexBufferView();
            vertexBufferView.BufferLocation = vertexBuffer.GPUVirtualAddress;
            vertexBufferView.StrideInBytes = Utilities.SizeOf<Vertex>();
            vertexBufferView.SizeInBytes = vertexBufferSize;


            //Create Index Buffer
            //Indices
            int[] indices = new int[]
            {
                0,1,2,0,2,3,
                4,6,5,4,7,6,
                8,9,10,8,10,11,
                12,14,13,12,15,14,
                16,18,17,16,19,18,
                20,21,22,20,22,23
            };
            int indexBufferSize = Utilities.SizeOf(indices);

            indexBuffer = device.CreateCommittedResource(new HeapProperties(HeapType.Upload), HeapFlags.None, ResourceDescription.Buffer(indexBufferSize), ResourceStates.GenericRead);

            // Copy the triangle data to the vertex buffer.
            IntPtr pIndexDataBegin = indexBuffer.Map(0);
            Utilities.Write(pIndexDataBegin, indices, 0, indices.Length);
            indexBuffer.Unmap(0);

            // Initialize the index buffer view.
            indexBufferView = new IndexBufferView();
            indexBufferView.BufferLocation = indexBuffer.GPUVirtualAddress;
            indexBufferView.Format = Format.R32_UInt;
            indexBufferView.SizeInBytes = indexBufferSize;

            //constant Buffer for each cubes
            constantBufferViewHeap = device.CreateDescriptorHeap(new DescriptorHeapDescription()
            {
                DescriptorCount = NumCubes,
                Type = DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView,
                Flags = DescriptorHeapFlags.ShaderVisible
            });

            int constantBufferSize = (Utilities.SizeOf<Transform>() + 255) & ~255;
            constantBuffer = device.CreateCommittedResource(new HeapProperties(HeapType.Upload), HeapFlags.None, ResourceDescription.Buffer(constantBufferSize * NumCubes), ResourceStates.GenericRead);
            constantBufferDescriptorSize = device.GetDescriptorHandleIncrementSize(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView);

            //First cube
            ConstantBufferViewDescription cbvDesc = new ConstantBufferViewDescription()
            {
                BufferLocation = constantBuffer.GPUVirtualAddress,
                SizeInBytes = constantBufferSize
            };

            CpuDescriptorHandle cbHandleHeapStart = constantBufferViewHeap.CPUDescriptorHandleForHeapStart;

            for (int i = 0; i < NumCubes; i++)
            {
                device.CreateConstantBufferView(cbvDesc, cbHandleHeapStart);
                cbvDesc.BufferLocation += Utilities.SizeOf<Transform>();
                cbHandleHeapStart += constantBufferDescriptorSize;
            }

            InitBundles();
        }

        private void InitBundles()
        {
            // Create and record the bundle.
            bundle = device.CreateCommandList(0, CommandListType.Bundle, bundleAllocator, pipelineState);
            bundle.SetGraphicsRootSignature(rootSignature);
            bundle.PrimitiveTopology = SharpDX.Direct3D.PrimitiveTopology.TriangleList;
            bundle.SetVertexBuffer(0, vertexBufferView);
            bundle.SetIndexBuffer(indexBufferView);

            bundle.SetDescriptorHeaps(1, new DescriptorHeap[] { constantBufferViewHeap });

            //first cube
            GpuDescriptorHandle heapStart = constantBufferViewHeap.GPUDescriptorHandleForHeapStart;

            for (int i = 0; i < NumCubes; i++)
            {
                bundle.SetGraphicsRootDescriptorTable(0, heapStart);
                bundle.DrawIndexedInstanced(36, 1, 0, 0, 0);
                heapStart += constantBufferDescriptorSize;
            }

            bundle.Close();

            // Create synchronization objects.
            {
                fence = device.CreateFence(0, FenceFlags.None);
                fenceValue = 1;

                // Create an event handle to use for frame synchronization.
                fenceEvent = new AutoResetEvent(false);
            }
        }

        private void PopulateCommandList()
        {
            // Command list allocators can only be reset when the associated 
            // command lists have finished execution on the GPU; apps should use 
            // fences to determine GPU execution progress.
            commandAllocator.Reset();

            // However, when ExecuteCommandList() is called on a particular command 
            // list, that command list can then be reset at any time and must be before 
            // re-recording.
            commandList.Reset(commandAllocator, pipelineState);


            // Set necessary state.
            commandList.SetGraphicsRootSignature(rootSignature);
            commandList.SetViewport(viewport);
            commandList.SetScissorRectangles(scissorRect);

            // Indicate that the back buffer will be used as a render target.
            commandList.ResourceBarrierTransition(renderTargets[frameIndex], ResourceStates.Present, ResourceStates.RenderTarget);

            CpuDescriptorHandle rtvHandle = renderTargetViewHeap.CPUDescriptorHandleForHeapStart;
            rtvHandle += frameIndex * rtvDescriptorSize;

            //pointer to depth stencil heap
            CpuDescriptorHandle dsvHandle = depthStencilViewHeap.CPUDescriptorHandleForHeapStart;

            //set render target and depth stencil
            commandList.SetRenderTargets(1, rtvHandle, dsvHandle);

            // Record commands.
            commandList.ClearRenderTargetView(rtvHandle, new Color4(0, 0.2F, 0.4f, 1), 0, null);
            commandList.ClearDepthStencilView(dsvHandle, ClearFlags.FlagsDepth, 1, 0);

            commandList.SetDescriptorHeaps(1, new DescriptorHeap[] { constantBufferViewHeap });
            commandList.ExecuteBundle(bundle);

            // Indicate that the back buffer will now be used to present.
            commandList.ResourceBarrierTransition(renderTargets[frameIndex], ResourceStates.RenderTarget, ResourceStates.Present);

            commandList.Close();
        }


        /// <summary> 
        /// Wait the previous command list to finish executing. 
        /// </summary> 
        private void WaitForPreviousFrame()
        {
            // WAITING FOR THE FRAME TO COMPLETE BEFORE CONTINUING IS NOT BEST PRACTICE. 
            // This is code implemented as such for simplicity. 

            int currentFence = fenceValue;
            commandQueue.Signal(fence, currentFence);
            fenceValue++;

            // Wait until the previous frame is finished.
            if (fence.CompletedValue < currentFence)
            {
                fence.SetEventOnCompletion(currentFence, fenceEvent.SafeWaitHandle.DangerousGetHandle());
                fenceEvent.WaitOne();
            }

            frameIndex = swapChain.CurrentBackBufferIndex;
        }

        public void Update()
        {
            float fov = (float)screenForm.ClientSize.Width / (float)screenForm.ClientSize.Height;
            Matrix projection = Matrix.PerspectiveFovLH((float)Math.PI / 3.0F, fov, 1, 10000);
            Matrix view = Matrix.LookAtLH(new Vector3(0, 10, 30), new Vector3(), Vector3.UnitY);
            Matrix world1 = Matrix.RotationY(Environment.TickCount / 1000.0F);

            Matrix world2 = Matrix.RotationY(Environment.TickCount / 1000.0F + 10) * Matrix.Translation(-10, 2, -20);

            Matrix world3 = Matrix.RotationY(Environment.TickCount / 1000.0F + 20) * Matrix.Translation(-20, 4, -40);

            Transform[] matrices = new Transform[]
            {
                new Transform() {WVP=world1*view*projection },
                new Transform() {WVP=world2*view*projection },
                new Transform() {WVP=world3*view*projection },
            };

            IntPtr pointer = constantBuffer.Map(0);
            Utilities.Write<Transform>(pointer, matrices, 0, matrices.Length);
            constantBuffer.Unmap(0);
        }


        public void Render()
        {
            // Record all the commands we need to render the scene into the command list.
            PopulateCommandList();

            // Execute the command list.
            commandQueue.ExecuteCommandList(commandList);

            // Present the frame.
            swapChain.Present(1, 0);

            WaitForPreviousFrame();
        }

        /// <summary>
        /// Dispose
        /// </summary>
        public void Dispose()
        {
            // Wait for the GPU to be done with all resources.
            WaitForPreviousFrame();

            //release all resources
            foreach (var target in renderTargets)
            {
                target.Dispose();
            }
            commandAllocator.Dispose();
            bundleAllocator.Dispose();
            commandQueue.Dispose();
            rootSignature.Dispose();
            renderTargetViewHeap.Dispose();
            pipelineState.Dispose();
            commandList.Dispose();
            bundle.Dispose();
            vertexBuffer.Dispose();
            fence.Dispose();
            swapChain.Dispose();
            device.Dispose();
        }


        struct Vertex
        {
            public Vector3 position;
            public Vector4 color;
            public Vertex(Vector3 position, Vector4 color)
            {
                this.position = position;
                this.color = color;
            }
        };

        struct Transform
        {
            public Matrix WVP;
            //need to reach 256 bits offset
            public Matrix Unused1;
            public Matrix Unused2;
            public Matrix Unused3;
        }

        private const int FrameCount = 2;
        private const int NumCubes = 3;

        private ViewportF viewport;
        private Rectangle scissorRect;
        // Pipeline objects.
        private SwapChain3 swapChain;
        private Device device;
        private Resource[] renderTargets = new Resource[FrameCount];
        private Resource depthTarget;
        private CommandAllocator commandAllocator;
        private CommandAllocator bundleAllocator;
        private CommandQueue commandQueue;
        private RootSignature rootSignature;
        private DescriptorHeap renderTargetViewHeap;
        private DescriptorHeap depthStencilViewHeap;
        private PipelineState pipelineState;
        private GraphicsCommandList commandList;
        private GraphicsCommandList bundle;
        private int rtvDescriptorSize;


        // App resources.
        Resource vertexBuffer;
        VertexBufferView vertexBufferView;
        Resource indexBuffer;
        IndexBufferView indexBufferView;

        //Constant Buffer
        private DescriptorHeap constantBufferViewHeap;
        Resource constantBuffer;
        private int constantBufferDescriptorSize;

        // Synchronization objects.
        private int frameIndex;
        private AutoResetEvent fenceEvent;

        private Fence fence;
        private int fenceValue;

        //
        private RenderForm screenForm;
    }
}
