﻿using Silk.NET.GLFW;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using WGPU.NET;
using Image = SixLabors.ImageSharp.Image;

namespace WGPU.Tests
{

	public static class Test
	{
		record struct Vec2(float X, float Y);
		record struct Vec3(float X, float Y, float Z);

		record struct Vec4(float X, float Y, float Z, float W);

		struct Vertex
		{
			public Vec3 Position;
			public Vec4 Color;
			public Vec2 UV;

			public Vertex(Vec3 position, Vec4 color, Vec2 uv)
			{
				Position = position;
				Color = color;
				UV = uv;
			}
		}

		struct UniformBuffer
		{
			public float Size;
		}

		public static void ErrorCallback(Wgpu.ErrorType type, string message)
		{
			var _message = message.Replace("\\r\\n", "\n");

			Console.WriteLine($"{type}: {_message}");

			Debugger.Break();
		}

		private static ErrorCallback errorCallback = new ErrorCallback(ErrorCallback);

		public static unsafe void Main(string[] args)
		{
			Glfw glfw = GlfwProvider.GLFW.Value;

			if (!glfw.Init())
			{
				Console.WriteLine("GLFW failed to initialize");
				Console.ReadKey();
				return;
			}

			glfw.WindowHint(WindowHintClientApi.ClientApi, ClientApi.NoApi);
			WindowHandle* window = glfw.CreateWindow(800, 600, "Hello WGPU.NET", null, null);

			if (window == null)
			{
				Console.WriteLine("Failed to open window");
				glfw.Terminate();
				Console.ReadKey();
				return;
			}

			var instance = new Instance();

			Surface surface;
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				var nativeWindow = new GlfwNativeWindow(glfw, window).Win32.Value;
				surface = instance.CreateSurfaceFromWindowsHWND(nativeWindow.HInstance, nativeWindow.Hwnd);


			}
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
			{
				var nativeWindow = new GlfwNativeWindow(glfw, window).X11.Value;
				surface = instance.CreateSurfaceFromXlibWindow(nativeWindow.Display, (uint)nativeWindow.Window);
			}
			else
			{
				var nativeWindow = new GlfwNativeWindow(glfw, window).Cocoa.Value;
				surface = instance.CreateSurfaceFromMetalLayer(nativeWindow);
			}


			Adapter adapter = default;

			instance.RequestAdapter(surface, default, default, (s, a, m) => adapter = a, Wgpu.BackendType.D3D12);

			adapter.GetProperties(out Wgpu.AdapterProperties properties);


			Device device = default;

			adapter.RequestDevice((s, d, m) => device = d,
				limits: new RequiredLimits()
				{
					Limits = new Wgpu.Limits()
					{
						maxBindGroups = 1
					}
				},
				deviceExtras: new DeviceExtras
				{
					Label = "Device"
				}
			);


			//TODO prevent Callback from being garbage collected
			device.SetUncapturedErrorCallback(errorCallback);


			Span<Vertex> vertices = new Vertex[]
			{
				new Vertex(new Vec3( -1,-1,0), new Vec4(1,1,0,1), new Vec2(-.2f,1.0f)),
				new Vertex(new Vec3(  1,-1,0), new Vec4(0,1,1,1), new Vec2(1.2f,1.0f)),
				new Vertex(new Vec3(  0, 1,0), new Vec4(1,0,1,1), new Vec2(0.5f,-.5f)),
			};


			var vertexBuffer = device.CreateBuffer("VertexBuffer", true, (ulong)(vertices.Length * sizeof(Vertex)), Wgpu.BufferUsage.Vertex);

			{
				Span<Vertex> mapped = vertexBuffer.GetMappedRange<Vertex>(0, vertices.Length);

				vertices.CopyTo(mapped);

				vertexBuffer.Unmap();
			}


			UniformBuffer uniformBufferData = new UniformBuffer
			{
				Size = 0.5f
			};

			var uniformBuffer = device.CreateBuffer("UniformBuffer", false, (ulong)sizeof(UniformBuffer), Wgpu.BufferUsage.Uniform | Wgpu.BufferUsage.CopyDst);

            

			var image = Image.Load<Rgba32>(Path.Combine("Resources","WGPU-Logo.png"));

			var imageSize = new Wgpu.Extent3D 
			{ 
				width = (uint)image.Width, 
				height = (uint)image.Height, 
				depthOrArrayLayers = 1 
			};

			var imageTexture = device.CreateTexture("Image", 
				usage: Wgpu.TextureUsage.TextureBinding | Wgpu.TextureUsage.CopyDst, 
				dimension: Wgpu.TextureDimension.TwoDimensions,
				size: imageSize,
				format: Wgpu.TextureFormat.RGBA8Unorm, 
				mipLevelCount: 1, 
				sampleCount: 1
			);

			{
				Span<Rgba32> pixels = new Rgba32[image.Width * image.Height];

				image.CopyPixelDataTo(pixels);

				device.GetQueue().WriteTexture<Rgba32>(
					destination: new ImageCopyTexture
					{
						Aspect = Wgpu.TextureAspect.All,
						MipLevel = 0,
						Origin = default,
						Texture = imageTexture
					},
					data: pixels, 
					dataLayout: new Wgpu.TextureDataLayout
					{
						bytesPerRow = (uint)(sizeof(Rgba32) * image.Width),
						offset = 0,
						rowsPerImage = (uint)image.Height
					},
					writeSize: imageSize
				);
			}

			var imageSampler = device.CreateSampler("ImageSampler",
				addressModeU: Wgpu.AddressMode.ClampToEdge,
				addressModeV: Wgpu.AddressMode.ClampToEdge,
				addressModeW: default,

				magFilter: Wgpu.FilterMode.Linear,
				minFilter: Wgpu.FilterMode.Linear,
				mipmapFilter: Wgpu.MipmapFilterMode.Linear,

				lodMinClamp: 0,
				lodMaxClamp: 1,
				compare: default,

				maxAnisotropy: 1
			);
			


			var bindGroupLayout = device.CreateBindgroupLayout(null, new Wgpu.BindGroupLayoutEntry[] {
				new Wgpu.BindGroupLayoutEntry
                {
					binding = 0,
					buffer = new Wgpu.BufferBindingLayout
                    {
						type = Wgpu.BufferBindingType.Uniform,
                    },
					visibility = (uint)Wgpu.ShaderStage.Vertex
                },
				new Wgpu.BindGroupLayoutEntry
				{
					binding = 1,
					sampler = new Wgpu.SamplerBindingLayout
                    {
						type = Wgpu.SamplerBindingType.Filtering
                    },
					visibility = (uint)Wgpu.ShaderStage.Fragment
				},
				new Wgpu.BindGroupLayoutEntry
				{
					binding = 2,
					texture = new Wgpu.TextureBindingLayout
                    {
						viewDimension = Wgpu.TextureViewDimension.TwoDimensions,
						sampleType = Wgpu.TextureSampleType.Float
                    },
					visibility = (uint)Wgpu.ShaderStage.Fragment
				}
			});

			var bindGroup = device.CreateBindGroup(null, bindGroupLayout, new BindGroupEntry[]
			{
				new BindGroupEntry
                {
					Binding = 0,
					Buffer = uniformBuffer
                },
				new BindGroupEntry
				{
					Binding = 1,
					Sampler = imageSampler
				},
				new BindGroupEntry
				{
					Binding = 2,
					TextureView = imageTexture.CreateTextureView("ImageTextureView", 
						format: Wgpu.TextureFormat.RGBA8Unorm, 
						dimension: Wgpu.TextureViewDimension.TwoDimensions,
						baseMipLevel: 0,
						mipLevelCount: 1,
						baseArrayLayer: 0,
						arrayLayerCount: 1,
						aspect: Wgpu.TextureAspect.All
					)
				}
			});



			var shader = device.CreateWgslShaderModule(
				label: "shader.wgsl",
				wgslCode: File.ReadAllText("shader.wgsl")
			);

			var pipelineLayout = device.CreatePipelineLayout(
				label: null,
				new BindGroupLayout[]
                {
					bindGroupLayout
                }
			);



			var vertexState = new VertexState()
			{
				Module = shader,
				EntryPoint = "vs_main",
				bufferLayouts = new VertexBufferLayout[]
                {
					new VertexBufferLayout
                    {
						ArrayStride = (ulong)sizeof(Vertex),
						Attributes = new Wgpu.VertexAttribute[]
                        {
							//position
							new Wgpu.VertexAttribute
                            {
								format = Wgpu.VertexFormat.Float32x3,
								offset = 0,
								shaderLocation = 0
                            },
							//color
							new Wgpu.VertexAttribute
							{
								format = Wgpu.VertexFormat.Float32x4,
								offset = (ulong)sizeof(Vec3), //right after positon
								shaderLocation = 1
							},
							//uv
							new Wgpu.VertexAttribute
							{
								format = Wgpu.VertexFormat.Float32x2,
								offset = (ulong)(sizeof(Vec3)+sizeof(Vec4)), //right after color
								shaderLocation = 2
							}
						}
                    }
                }
			};

			var swapChainFormat = surface.GetPreferredFormat(adapter);

			var fragmentState = new FragmentState()
			{
				Module = shader,
				EntryPoint = "fs_main",
				colorTargets = new ColorTargetState[]
				{
				new ColorTargetState()
				{
					Format = swapChainFormat,
					BlendState = new Wgpu.BlendState()
					{
						color = new Wgpu.BlendComponent()
						{
							srcFactor = Wgpu.BlendFactor.One,
							dstFactor = Wgpu.BlendFactor.Zero,
							operation = Wgpu.BlendOperation.Add
						},
						alpha = new Wgpu.BlendComponent()
						{
							srcFactor = Wgpu.BlendFactor.One,
							dstFactor = Wgpu.BlendFactor.Zero,
							operation = Wgpu.BlendOperation.Add
						}
					},
					WriteMask = (uint)Wgpu.ColorWriteMask.All
				}
				}
			};

			var renderPipeline = device.CreateRenderPipeline(
				label: "Render pipeline",
				layout: pipelineLayout,
				vertexState: vertexState,
				primitiveState: new Wgpu.PrimitiveState()
				{
					topology = Wgpu.PrimitiveTopology.TriangleList,
					stripIndexFormat = Wgpu.IndexFormat.Undefined,
					frontFace = Wgpu.FrontFace.CCW,
					cullMode = Wgpu.CullMode.None
				},
				multisampleState: new Wgpu.MultisampleState()
				{
					count = 1,
					mask = uint.MaxValue,
					alphaToCoverageEnabled = false
				},
				fragmentState: fragmentState
			);

			glfw.GetWindowSize(window, out int prevWidth, out int prevHeight);


			var swapChainDescriptor = new Wgpu.SwapChainDescriptor
			{
				usage = (uint)Wgpu.TextureUsage.RenderAttachment,
				format = swapChainFormat,
				width = (uint)prevWidth,
				height = (uint)prevHeight,
				presentMode = Wgpu.PresentMode.Fifo
			};

			var swapChain = device.CreateSwapChain(surface, swapChainDescriptor);


			var isReady = new AutoResetEvent(false);

			Span<UniformBuffer> uniformBufferSpan = stackalloc UniformBuffer[1];

			var startTime = DateTime.Now;

			while (!glfw.WindowShouldClose(window))
			{
				TimeSpan duration = DateTime.Now - startTime;

				uniformBufferData.Size = (float)(1 + 0.5 * Math.Sin(duration.TotalSeconds*2.0));



				glfw.GetWindowSize(window, out int width, out int height);

				if ((width != prevWidth || height != prevHeight) && width != 0 && height != 0 )
				{
					prevWidth = width;
					prevHeight = height;
					swapChainDescriptor.width = (uint)width;
					swapChainDescriptor.height = (uint)height;
					swapChain = device.CreateSwapChain(surface, swapChainDescriptor);
				}

				var nextTexture = swapChain.GetCurrentTextureView();

				if (nextTexture == null)
				{
					Console.WriteLine("Could not acquire next swap chain texture");
					return;
				}

				var encoder = device.CreateCommandEncoder("Command Encoder");

				var renderPass = encoder.BeginRenderPass(
					label: null,
					colorAttachments: new RenderPassColorAttachment[]
					{
					new RenderPassColorAttachment()
					{
						view = nextTexture,
						resolveTarget = default,
						loadOp = Wgpu.LoadOp.Clear,
						storeOp = Wgpu.StoreOp.Store,
						clearValue = new Wgpu.Color() { r = 0, g = 0.02f, b = 0.1f, a = 1 }
					}
					},
					depthStencilAttachment: null
				);

				renderPass.SetPipeline(renderPipeline);

				renderPass.SetBindGroup(0, bindGroup, Array.Empty<uint>());
				renderPass.SetVertexBuffer(0, vertexBuffer, 0, (ulong)(vertices.Length * sizeof(Vertex)));
				renderPass.Draw(3, 1, 0, 0);
				renderPass.End();



				var queue = device.GetQueue();

				uniformBufferSpan[0] = uniformBufferData;

				queue.WriteBuffer<UniformBuffer>(uniformBuffer, 0, uniformBufferSpan);


				var commandBuffer = encoder.Finish(null);

				queue.Submit(new CommandBuffer[]
				{
					commandBuffer
				});

				swapChain.Present();


				glfw.PollEvents();
			}

			glfw.DestroyWindow(window);
			glfw.Terminate();
		}
	}
}