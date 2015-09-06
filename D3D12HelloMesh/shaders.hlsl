cbuffer ConstantBuffer : register(b0)
{
	float4x4 transform;
	float4x4 world;
	float4 lightDirection;
	float4 camera;//xyz camera, Z specular
};
Texture2D textureMap:register(t0);
SamplerState textureSampler:register(s0);

struct VSInput
{
	float4 position : POSITION;
	float3 normal : NORMAL;
	float3 texcoord : TEXCOORD;
};

struct PSInput
{
	float4 position : SV_POSITION;
	float3 normal : NORMAL;
	float3 texcoord : TEXCOORD;
};

PSInput VSMain(VSInput input)
{
	PSInput result;

	result.position = mul(transform, input.position);
	result.normal =mul(world,input.normal);
	result.texcoord = input.texcoord;
	return result;
}

float4 PSMain(PSInput input) : SV_TARGET
{
	float4 color = textureMap.Sample(textureSampler, input.texcoord.xy);
	float3 N = normalize(input.normal);
	float D = dot(lightDirection.xyz,N);

	float specularPower = 30;
	
	float3 R = normalize(2 * dot(lightDirection, N) * N - lightDirection.xyz);
	float3 V = normalize(camera.xyz);

	float S = max(pow(dot(R, V), camera.w), 0)*D;
	
	return color*saturate(D) +saturate(S)*D;
}