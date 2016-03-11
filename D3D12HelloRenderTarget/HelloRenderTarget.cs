using SharpDX.DXGI;
using System.Threading;
using System;
using Assimp;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace D3D12HelloRenderTarget
{
    using SharpDX;
    using SharpDX.Direct3D12;
    using SharpDX.Windows;

    public class HelloMesh
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

            form.KeyDown += (sender, e) =>
            {
                switch (e.KeyCode)
                {
                    case System.Windows.Forms.Keys.D1:
                        effectType = 0;
                        break;
                    case System.Windows.Forms.Keys.D2:
                        effectType = 1;
                        break;
                    case System.Windows.Forms.Keys.D3:
                        effectType = 2;
                        break;
                    default:
                        break;
                }
            };

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
            renderTargetViewHeap = device.CreateDescriptorHeap(new DescriptorHeapDescription()
            {
                DescriptorCount = FrameCount + 1,
                Flags = DescriptorHeapFlags.None,
                Type = DescriptorHeapType.RenderTargetView
            });
            rtvDescriptorSize = device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);

            //create depth buffer;
            depthStencilViewHeap = device.CreateDescriptorHeap(new DescriptorHeapDescription()
            {
                DescriptorCount = FrameCount + 1,
                Flags = DescriptorHeapFlags.None,
                Type = DescriptorHeapType.DepthStencilView
            });

            //constant buffer view heap
            resourceViewHeap = device.CreateDescriptorHeap(new DescriptorHeapDescription()
            {
                DescriptorCount = 100,
                Type = DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView,
                Flags = DescriptorHeapFlags.ShaderVisible
            });

            //Create targets
            CreateTargets(width, height);

            //sampler buffer view heap
            samplerViewHeap = device.CreateDescriptorHeap(new DescriptorHeapDescription()
            {
                DescriptorCount = 10,
                Type = DescriptorHeapType.Sampler,
                Flags = DescriptorHeapFlags.ShaderVisible
            });

            //bind sampler
            device.CreateSampler(new SamplerStateDescription()
            {
                Filter = Filter.ComparisonMinMagMipLinear,
                AddressU = TextureAddressMode.Wrap,
                AddressV = TextureAddressMode.Wrap,
                AddressW = TextureAddressMode.Wrap,
                MinimumLod = float.MinValue,
                MaximumLod = float.MaxValue,
                MipLodBias = 0,
                MaximumAnisotropy = 0,
                ComparisonFunction = Comparison.Never
            }, samplerViewHeap.CPUDescriptorHandleForHeapStart);

            commandAllocator = device.CreateCommandAllocator(CommandListType.Direct);
            bundleAllocator = device.CreateCommandAllocator(CommandListType.Bundle);

            form.UserResized += (sender, e) =>
            {
                isResizing = true;
            };
        }

        private void CreateTargets(int width, int height)
        {
            //Viewport and scissorrect
            viewport.Width = width;
            viewport.Height = height;
            viewport.MaxDepth = 1.0f;

            scissorRect.Right = width;
            scissorRect.Bottom = height;

            // Create frame resources.
            CpuDescriptorHandle rtvHandle = renderTargetViewHeap.CPUDescriptorHandleForHeapStart;
            for (int n = 0; n < FrameCount; n++)
            {
                renderTargets[n] = swapChain.GetBackBuffer<Resource>(n);
                device.CreateRenderTargetView(renderTargets[n], null, rtvHandle);
                rtvHandle += rtvDescriptorSize;
            }

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
        }

        private void LoadAssets()
        {
            RootParameter parameter1 = new RootParameter(ShaderVisibility.All, new DescriptorRange() { RangeType = DescriptorRangeType.ConstantBufferView, BaseShaderRegister = 0, DescriptorCount = 1 });
            RootParameter parameter2 = new RootParameter(ShaderVisibility.Pixel, new DescriptorRange() { RangeType = DescriptorRangeType.ShaderResourceView, BaseShaderRegister = 0, DescriptorCount = 1 });
            RootParameter parameter3 = new RootParameter(ShaderVisibility.Pixel, new DescriptorRange() { RangeType = DescriptorRangeType.Sampler, BaseShaderRegister = 0, DescriptorCount = 1 });

            // Create a root signature.
            RootSignatureDescription rootSignatureDesc = new RootSignatureDescription(RootSignatureFlags.AllowInputAssemblerInputLayout, new RootParameter[] { parameter1, parameter2, parameter3 });
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
                    new InputElement("NORMAL",0,Format.R32G32B32_Float,12,0),
                    new InputElement("TEXCOORD",0,Format.R32G32B32_Float,24,0)
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

            //constant Buffer 
            int constantBufferSize = (Utilities.SizeOf<Transform>() + 255) & ~255;
            constantBuffer = device.CreateCommittedResource(new HeapProperties(HeapType.Upload), HeapFlags.None, ResourceDescription.Buffer(constantBufferSize), ResourceStates.GenericRead);

            //constant buffer
            ConstantBufferViewDescription cbvDesc = new ConstantBufferViewDescription()
            {
                BufferLocation = constantBuffer.GPUVirtualAddress,
                SizeInBytes = constantBufferSize
            };

            CpuDescriptorHandle cbHandleHeapStart = resourceViewHeap.CPUDescriptorHandleForHeapStart + constantBufferViewPosition * device.GetDescriptorHandleIncrementSize(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView);
            device.CreateConstantBufferView(cbvDesc, cbHandleHeapStart);

            //Render target
            LoadRenderTargetData();

            //load mesh
            LoadMesh();

            // Create synchronization objects.
            {
                fence = device.CreateFence(0, FenceFlags.None);
                fenceValue = 1;

                // Create an event handle to use for frame synchronization.
                fenceEvent = new AutoResetEvent(false);
            }



            InitBundles();
        }

        private void LoadRenderTargetData()
        {
            // Define the geometry for a quad.
            Vertex[] quadBuffer = new Vertex[]
            {
                    new Vertex() {position=new Vector3(-1, -1, 0 ),textureCoordinate=new Vector3(0,1,0)},
                    new Vertex() {position=new Vector3(-1, 1, 0),textureCoordinate=new Vector3(0,0,0)},
                    new Vertex() {position=new Vector3(1, -1, 0),textureCoordinate=new Vector3(1,1,0)},

                    new Vertex() {position=new Vector3(-1, 1, 0 ),textureCoordinate=new Vector3(0,0,0)},
                    new Vertex() {position=new Vector3(1, 1, 0),textureCoordinate=new Vector3(1,0,0)},
                    new Vertex() {position=new Vector3(1, -1, 0),textureCoordinate=new Vector3(1,1,0)},
            };

            int vertexBufferSize = Utilities.SizeOf(quadBuffer);

            quadVertexBuffer = device.CreateCommittedResource(new HeapProperties(HeapType.Upload), HeapFlags.None, ResourceDescription.Buffer(vertexBufferSize), ResourceStates.GenericRead);

            IntPtr pointer = quadVertexBuffer.Map(0);
            Utilities.Write(pointer, quadBuffer, 0, 6);
            quadVertexBuffer.Unmap(0);


            quadVertexBufferView = new VertexBufferView();
            quadVertexBufferView.BufferLocation = quadVertexBuffer.GPUVirtualAddress;
            quadVertexBufferView.StrideInBytes = Utilities.SizeOf<Vertex>();
            quadVertexBufferView.SizeInBytes = vertexBufferSize;


            //Render Target
            ClearValue renderTargetOptimizedClearValue = new ClearValue()
            {
                Format = Format.R8G8B8A8_UNorm,
                Color = new Vector4(0, 0.2F, 0.4f, 1)
            };
            postProcessingRenderTarget = device.CreateCommittedResource(new HeapProperties(HeapType.Default), HeapFlags.None,
                 new ResourceDescription(ResourceDimension.Texture2D, 0, TargetSize, TargetSize, 1, 0, Format.R8G8B8A8_UNorm, 1, 0, TextureLayout.Unknown, ResourceFlags.AllowRenderTarget), ResourceStates.RenderTarget, renderTargetOptimizedClearValue);

            CpuDescriptorHandle rtvHandle = renderTargetViewHeap.CPUDescriptorHandleForHeapStart + device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView) * 2;
            device.CreateRenderTargetView(postProcessingRenderTarget, null, rtvHandle);


            //Shader Resource View
            int D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING = 5768;
            ShaderResourceViewDescription desc = new ShaderResourceViewDescription
            {
                Dimension = ShaderResourceViewDimension.Texture2D,
                Format = postProcessingRenderTarget.Description.Format,
                Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING,
            };
            desc.Texture2D.MipLevels = 1;
            desc.Texture2D.MostDetailedMip = 0;
            desc.Texture2D.ResourceMinLODClamp = 0;

            //Create Render Target View
            //position start after first constant buffer and textures
            device.CreateShaderResourceView(postProcessingRenderTarget, desc, resourceViewHeap.CPUDescriptorHandleForHeapStart + device.GetDescriptorHandleIncrementSize(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView) * renderTargetViewPosition);

            //Depth Target
            ClearValue depthOptimizedClearValue = new ClearValue()
            {
                Format = Format.D32_Float,
                DepthStencil = new DepthStencilValue() { Depth = 1.0F, Stencil = 0 },
            };

            postProcessingDepthTarget = device.CreateCommittedResource(new HeapProperties(HeapType.Default), HeapFlags.None,
                 new ResourceDescription(ResourceDimension.Texture2D, 0, TargetSize, TargetSize, 1, 0, Format.D32_Float, 1, 0, TextureLayout.Unknown, ResourceFlags.AllowDepthStencil), ResourceStates.DepthWrite, depthOptimizedClearValue);

            CpuDescriptorHandle dpsHandle = depthStencilViewHeap.CPUDescriptorHandleForHeapStart + device.GetDescriptorHandleIncrementSize(DescriptorHeapType.DepthStencilView);
            device.CreateDepthStencilView(postProcessingDepthTarget, null, dpsHandle);


            //Shader

#if DEBUG
            var vertexShader = new ShaderBytecode(SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile("postProcessing.hlsl", "VSMain", "vs_5_0", SharpDX.D3DCompiler.ShaderFlags.Debug));
#else
            var vertexShader = new ShaderBytecode(SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile("shaders.hlsl", "VSMain", "vs_5_0"));
#endif

#if DEBUG
            var pixelShader = new ShaderBytecode(SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile("postProcessing.hlsl", "PSMain", "ps_5_0", SharpDX.D3DCompiler.ShaderFlags.Debug));
#else
            var pixelShader = new ShaderBytecode(SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile("shaders.hlsl", "PSMain", "ps_5_0"));
#endif

            // Define the vertex input layout.
            InputElement[] inputElementDescs = new InputElement[]
            {
                    new InputElement("POSITION",0,Format.R32G32B32_Float,0,0),
                    new InputElement("NORMAL",0,Format.R32G32B32_Float,12,0),
                    new InputElement("TEXCOORD",0,Format.R32G32B32_Float,24,0)
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

            postProcessingPipelineState = device.CreateGraphicsPipelineState(psoDesc);

            //constant Buffer 
            int constantBufferSize = (Utilities.SizeOf<PostProcessingData>() + 255) & ~255;
            postProcessingConstantBuffer = device.CreateCommittedResource(new HeapProperties(HeapType.Upload), HeapFlags.None, ResourceDescription.Buffer(constantBufferSize), ResourceStates.GenericRead);

            //constant buffer
            ConstantBufferViewDescription cbvDesc = new ConstantBufferViewDescription()
            {
                BufferLocation = postProcessingConstantBuffer.GPUVirtualAddress,
                SizeInBytes = constantBufferSize
            };

            var heapPosition = resourceViewHeap.CPUDescriptorHandleForHeapStart + device.GetDescriptorHandleIncrementSize(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView) * renderTargetConstantBufferPosition;
            device.CreateConstantBufferView(cbvDesc, heapPosition);
        }

        private void LoadMesh()
        {
            SamplerStateDescription samplerDesc = new SamplerStateDescription()
            {
                Filter = Filter.ComparisonMinMagMipLinear,
                AddressU = TextureAddressMode.Wrap,
                AddressV = TextureAddressMode.Wrap,
                AddressW = TextureAddressMode.Wrap,
                MinimumLod = float.MinValue,
                MaximumLod = float.MaxValue,
                MipLodBias = 0,
                MaximumAnisotropy = 0,
                ComparisonFunction = Comparison.Never
            };

            // Load model from obj.
            var importer = new Assimp.AssimpContext();
            var scene = importer.ImportFile(@"../../../Models/lara/lara.obj", PostProcessSteps.GenerateSmoothNormals | PostProcessSteps.FlipUVs | PostProcessSteps.PreTransformVertices);


            Vertex[] vertices = new Vertex[scene.Meshes.Sum(m => m.VertexCount)];
            int[] indices = new int[scene.Meshes.Sum(m => m.FaceCount * 3)];
            faceCounts = new List<int>();

            int vertexOffSet = 0;
            int indexOffSet = 0;
            int k = 0;
            foreach (var mesh in scene.Meshes)
            {
                var positions = mesh.Vertices;
                var normals = mesh.Normals;
                var texs = mesh.TextureCoordinateChannels[0];
                for (int i = 0; i < mesh.VertexCount; i++)
                {
                    vertices[vertexOffSet + i] = new Vertex()
                    {
                        position = new Vector3(positions[i].X, positions[i].Y, positions[i].Z),
                        normal = new Vector3(normals[i].X, normals[i].Y, normals[i].Z),
                        textureCoordinate = new Vector3(texs[i].X, texs[i].Y, texs[i].Z)
                    };
                }

                var faces = mesh.Faces;
                for (int i = 0; i < mesh.FaceCount; i++)
                {
                    indices[i * 3 + indexOffSet] = (int)faces[i].Indices[0] + vertexOffSet;
                    indices[i * 3 + 1 + indexOffSet] = (int)faces[i].Indices[1] + vertexOffSet;
                    indices[i * 3 + 2 + indexOffSet] = (int)faces[i].Indices[2] + vertexOffSet;
                }

                faceCounts.Add(mesh.FaceCount * 3);
                vertexOffSet += mesh.VertexCount;
                indexOffSet += mesh.FaceCount * 3;

                string textureName = System.IO.Path.GetFileName(scene.Materials[mesh.MaterialIndex].TextureDiffuse.FilePath);
                var texResource = TextureUtilities.CreateTextureFromDDS(device, @"../../../Models/lara/" + textureName);
                textures.Add(texResource);

                int D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING = 5768;
                ShaderResourceViewDescription desc = new ShaderResourceViewDescription
                {
                    Dimension = ShaderResourceViewDimension.Texture2D,
                    Format = texResource.Description.Format,
                    Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING,
                };
                desc.Texture2D.MipLevels = 1;
                desc.Texture2D.MostDetailedMip = 0;
                desc.Texture2D.ResourceMinLODClamp = 0;

                device.CreateShaderResourceView(texResource, desc, resourceViewHeap.CPUDescriptorHandleForHeapStart + (k + meshTexturePosition) * device.GetDescriptorHandleIncrementSize(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView));
                k++;
            }

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

        }

        private void InitBundles()
        {

            // Create and record the bundle.
            bundleAllocator.Reset();
            bundle = device.CreateCommandList(0, CommandListType.Bundle, bundleAllocator, pipelineState);

            //Set Heap
            bundle.SetDescriptorHeaps(2, new DescriptorHeap[] { resourceViewHeap, samplerViewHeap });

            bundle.SetGraphicsRootSignature(rootSignature);
            bundle.PrimitiveTopology = SharpDX.Direct3D.PrimitiveTopology.TriangleList;
            bundle.SetVertexBuffer(0, vertexBufferView);
            bundle.SetIndexBuffer(indexBufferView);


            //model
            GpuDescriptorHandle heapStart = resourceViewHeap.GPUDescriptorHandleForHeapStart;

            //constant buffer
            bundle.SetGraphicsRootDescriptorTable(0, heapStart + constantBufferViewPosition * device.GetDescriptorHandleIncrementSize(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView));

            //sampler
            bundle.SetGraphicsRootDescriptorTable(2, samplerViewHeap.GPUDescriptorHandleForHeapStart);

            //set materials and draw
            int offset = 0;
            int k = 0;
            foreach (var count in faceCounts)
            {
                //Texture coordinates start after constant buffer
                bundle.SetGraphicsRootDescriptorTable(1, heapStart + (meshTexturePosition + k) * device.GetDescriptorHandleIncrementSize(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView));
                bundle.DrawIndexedInstanced(count, 1, offset, 0, 0);
                offset += count;
                k++;
            }



            bundle.Close();


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

            // Indicate that the back buffer will be used as a render target.
            commandList.ResourceBarrierTransition(renderTargets[frameIndex], ResourceStates.Present, ResourceStates.RenderTarget);


            //draw bundle on render target
            commandList.SetRenderTargets(1, renderTargetViewHeap.CPUDescriptorHandleForHeapStart + device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView) * renderTargetPosition,
                depthStencilViewHeap.CPUDescriptorHandleForHeapStart + device.GetDescriptorHandleIncrementSize(DescriptorHeapType.DepthStencilView));
            commandList.SetViewport(new ViewportF(0, 0, TargetSize, TargetSize));
            commandList.SetScissorRectangles(new Rectangle(0, 0, TargetSize, TargetSize));


            commandList.ClearRenderTargetView(renderTargetViewHeap.CPUDescriptorHandleForHeapStart + device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView) * 2, new Color4(0, 0.2F, 0.4f, 1), 0, null);
            commandList.ClearDepthStencilView(depthStencilViewHeap.CPUDescriptorHandleForHeapStart + device.GetDescriptorHandleIncrementSize(DescriptorHeapType.DepthStencilView), ClearFlags.FlagsDepth, 1, 0);

            //set all heap
            commandList.SetDescriptorHeaps(2, new DescriptorHeap[] { resourceViewHeap, samplerViewHeap });
            commandList.ExecuteBundle(bundle);



            //========================================
            //Draw quad on swapchain

            // Set necessary state.
            commandList.SetGraphicsRootSignature(rootSignature);
            commandList.SetGraphicsRootDescriptorTable(0, resourceViewHeap.GPUDescriptorHandleForHeapStart + renderTargetConstantBufferPosition * device.GetDescriptorHandleIncrementSize(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView));
            commandList.SetViewport(viewport);
            commandList.SetScissorRectangles(scissorRect);

            CpuDescriptorHandle rtvHandle = renderTargetViewHeap.CPUDescriptorHandleForHeapStart;
            rtvHandle += frameIndex * rtvDescriptorSize;

            //pointer to depth stencil heap
            CpuDescriptorHandle dsvHandle = depthStencilViewHeap.CPUDescriptorHandleForHeapStart;


            //set render target and depth stencil
            commandList.SetRenderTargets(1, rtvHandle, dsvHandle);

            // Record commands.
            commandList.ClearRenderTargetView(rtvHandle, new Color4(0, 0.2F, 0.4f, 1), 0, null);
            commandList.ClearDepthStencilView(dsvHandle, ClearFlags.FlagsDepth, 1, 0);

            //Post processing
            commandList.PipelineState = postProcessingPipelineState;

            commandList.SetVertexBuffer(0, quadVertexBufferView);
            GpuDescriptorHandle heapStart = resourceViewHeap.GPUDescriptorHandleForHeapStart + device.GetDescriptorHandleIncrementSize(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView) * renderTargetViewPosition;
            commandList.SetGraphicsRootDescriptorTable(1, heapStart);

            commandList.DrawInstanced(6, 1, 0, 0);

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
            Vector3 eye = new Vector3(0, 10, 22);
            Vector3 target = new Vector3(0, 10, 0);
            Matrix view = Matrix.LookAtLH(eye, target, Vector3.UnitY);
            Matrix world = Matrix.Scaling(10, 10, 10) * Matrix.RotationY(Environment.TickCount / 1000.0F);
            Vector3 L = new Vector3(10, 10, 30);
            L.Normalize();

            Transform transform = new Transform
            {
                WVP = world * view * projection,
                world = world,
                lightDirection = new Vector4(L, 0),
                camera = new Vector4(eye - target, 30)
            };

            IntPtr pointer = constantBuffer.Map(0);
            Utilities.Write<Transform>(pointer, ref transform);
            constantBuffer.Unmap(0);

            //Set second buffer

            PostProcessingData data = new PostProcessingData();
            data.data = new Vector4(effectType, 0, TargetSize, TargetSize);

            pointer = postProcessingConstantBuffer.Map(0);
            Utilities.Write<PostProcessingData>(pointer, ref data);
            postProcessingConstantBuffer.Unmap(0);
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

            if (isResizing)
            {
                fenceValue += FrameCount;
                for (int i = 0; i < FrameCount; i++)
                {
                    renderTargets[i].Dispose();
                }
                depthTarget.Dispose();
                swapChain.ResizeBuffers(FrameCount, screenForm.ClientSize.Width, screenForm.ClientSize.Height, Format.R8G8B8A8_UNorm, SwapChainFlags.None);
                CreateTargets(screenForm.ClientSize.Width, screenForm.ClientSize.Height);
                WaitForPreviousFrame();
                isResizing = false;
            }
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
            public Vector3 normal;
            public Vector3 textureCoordinate;
        };

        struct Transform
        {
            public Matrix WVP;
            public Matrix world;
            public Vector4 lightDirection;
            public Vector4 camera;

            //need to reach 256 bits offset
            public Vector4 unusedV1;
            public Vector4 unusedV2;
            public Matrix unused3;
        }

        struct PostProcessingData
        {
            public Vector4 data;
            public Vector4 unusedV1;
            public Vector4 unusedV2;
            public Vector4 unusedV3;

            //need to reach 256 bits offset
            public Matrix unused1;
            public Matrix unused2;
            public Matrix unused3;
        }

        private const int FrameCount = 2;

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

        //heap
        private DescriptorHeap renderTargetViewHeap;
        private DescriptorHeap depthStencilViewHeap;
        private DescriptorHeap samplerViewHeap;
        private DescriptorHeap resourceViewHeap;

        //
        private PipelineState pipelineState;
        private GraphicsCommandList commandList;
        private GraphicsCommandList bundle;
        private int rtvDescriptorSize;

        //Render Target
        Resource quadVertexBuffer;
        VertexBufferView quadVertexBufferView;
        const int TargetSize = 1024;
        Resource postProcessingRenderTarget;
        Resource postProcessingDepthTarget;

        //Heap position
        int renderTargetPosition = FrameCount;//after the 2 frames

        //post processing pipeline
        PipelineState postProcessingPipelineState;
        float effectType = 0;

        // Mesh Data
        Resource vertexBuffer;
        VertexBufferView vertexBufferView;
        Resource indexBuffer;
        IndexBufferView indexBufferView;
        List<int> faceCounts;
        List<Resource> textures = new List<Resource>();


        //Constant Buffer
        Resource constantBuffer;
        Resource postProcessingConstantBuffer;

        //Heap position
        int renderTargetViewPosition = 0;
        int constantBufferViewPosition = 1;
        int renderTargetConstantBufferPosition = 2;
        int meshTexturePosition = 3;

        // Synchronization objects.
        private int frameIndex;
        private AutoResetEvent fenceEvent;

        private Fence fence;
        private int fenceValue;

        //
        private RenderForm screenForm;
        private bool isResizing = false;
    }
}
