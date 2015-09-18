cbuffer ConstantBuffer : register(b0)
{
	float4 data;
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
	float2 texcoord : TEXCOORD;
};

PSInput VSMain(VSInput input)
{
	PSInput result;

	result.position = input.position;
	result.texcoord = input.texcoord.xy;
	return result;
}

float4 PSMain(PSInput input) : SV_TARGET
{
	switch (data.x)
	{
	case 0:
	{
		float4 s22 = textureMap.Sample(textureSampler, input.texcoord); // center
		float4 s11 = textureMap.Sample(textureSampler, input.texcoord + float2(-1.0f / 1024.0f, -1.0f / 768.0f));
		float4 s33 = textureMap.Sample(textureSampler, input.texcoord + float2(1.0f / 1024.0f, 1.0f / 768.0f));

		s11.xyz = (s11.x + s11.y + s11.z);
		s22.xyz = (s22.x + s22.y + s22.z) * -0.5;
		s33.xyz = (s22.x + s22.y + s22.z) * 0.2;

		return (s11 + s22 + s33);

	}
	case 1:
	{
		float4 lum = float4(0.30, 0.59, 0.11, 1);

		// TOP ROW
		float s11 = dot(textureMap.Sample(textureSampler, input.texcoord + float2(-1.0f / 1024.0f, -1.0f / 768.0f)), lum);   // LEFT
		float s12 = dot(textureMap.Sample(textureSampler, input.texcoord + float2(0, -1.0f / 768.0f)), lum);             // MIDDLE
		float s13 = dot(textureMap.Sample(textureSampler, input.texcoord + float2(1.0f / 1024.0f, -1.0f / 768.0f)), lum);    // RIGHT

																															 // MIDDLE ROW
		float s21 = dot(textureMap.Sample(textureSampler, input.texcoord + float2(-1.0f / 1024.0f, 0)), lum);                // LEFT
																															 // Omit center
		float s23 = dot(textureMap.Sample(textureSampler, input.texcoord + float2(-1.0f / 1024.0f, 0)), lum);                // RIGHT

																															 // LAST ROW
		float s31 = dot(textureMap.Sample(textureSampler, input.texcoord + float2(-1.0f / 1024.0f, 1.0f / 768.0f)), lum);    // LEFT
		float s32 = dot(textureMap.Sample(textureSampler, input.texcoord + float2(0, 1.0f / 768.0f)), lum);              // MIDDLE
		float s33 = dot(textureMap.Sample(textureSampler, input.texcoord + float2(1.0f / 1024.0f, 1.0f / 768.0f)), lum); // RIGHT

																														 
		float t1 = s13 + s33 + (2 * s23) - s11 - (2 * s21) - s31;
		float t2 = s31 + (2 * s32) + s33 - s11 - (2 * s12) - s13;

		float4 col;

		if (((t1 * t1) + (t2 * t2)) > 0.1) {
			col = float4(1, 1, 1, 1);
		}
		else {
			col = float4(0, 0.2F, 0.4f, 1);
		}

		return col;
	}
	case 2:
		return textureMap.Sample(textureSampler, input.texcoord);
	}

return 0;

}