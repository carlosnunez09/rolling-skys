#ifndef ROLLING_SKYS_CAMERA_CUTAWAY_INCLUDED
#define ROLLING_SKYS_CAMERA_CUTAWAY_INCLUDED

float4 _CameraCutawayTarget;
float4 _CameraCutawayCamera;
// Radius in metres, soft edge fraction, enabled, near clip distance.
float4 _CameraCutawaySettings;

void ApplyCameraCutaway(float3 positionWS, float2 pixelPosition, float exempt)
{
    if (_CameraCutawaySettings.z < 0.5 || exempt > 0.5) return;
    float3 axis = _CameraCutawayTarget.xyz - _CameraCutawayCamera.xyz;
    float lengthToTarget = length(axis);
    if (lengthToTarget < 0.1) return;
    axis /= lengthToTarget;
    float3 relative = positionWS - _CameraCutawayCamera.xyz;
    float along = dot(relative, axis);
    // Never remove terrain behind the car. Start slightly behind the near plane.
    if (along < 0.0 || along > lengthToTarget - 0.05) return;
    float radialDistance = length(relative - axis * along);
    float radius = max(_CameraCutawaySettings.x, 0.01);
    float visibility = smoothstep(radius * (1.0 - _CameraCutawaySettings.y), radius, radialDistance);
    // Stable screen-space dithering avoids transparent sorting and retains opaque toon lighting.
    float threshold = frac(52.9829189 * frac(dot(floor(pixelPosition), float2(0.06711056, 0.00583715))));
    clip(visibility - max(threshold, 0.001));
}
#endif
