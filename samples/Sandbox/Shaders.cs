namespace Sandbox;

internal static class Shaders
{
    // ---- Main forward pass, shared vertex shader for every variant below ----

    public const string Vertex = """
        struct PushConstants
        {
            float4x4 model;
        };
        [[vk::push_constant]] PushConstants pc;

        [[vk::binding(0, 0)]]
        cbuffer FrameConstants : register(b0)
        {
            float4x4 viewProjection;
            float4x4 lightViewProjection;
            float4 lightDirection;
        };

        struct VSInput
        {
            float3 position : POSITION;
            float3 normal : NORMAL;
            float2 uv : TEXCOORD0;
        };

        struct VSOutput
        {
            float4 position : SV_Position;
            float3 worldNormal : NORMAL;
            float2 uv : TEXCOORD0;
            float4 lightClipPos : TEXCOORD1;
            float3 worldPos : TEXCOORD2;
        };

        VSOutput main(VSInput input)
        {
            VSOutput output;
            float4 worldPos = mul(float4(input.position, 1.0), pc.model);
            output.position = mul(worldPos, viewProjection);
            output.worldNormal = mul(float4(input.normal, 0.0), pc.model).xyz;
            output.uv = input.uv;
            output.lightClipPos = mul(worldPos, lightViewProjection);
            output.worldPos = worldPos.xyz;
            return output;
        }
        """;

    // ---- Fallback fragment shader (no ray tracing on this GPU): shadow-map shadows only for the outdoor sun,
    // no shadow at all for the room's point light (there's no shadow map for something that moves every frame),
    // flat push-constant albedo or the procedural ground grid, reflectivity ignored (nothing to trace against). ----

    public const string Fragment = """
        [[vk::binding(0, 0)]]
        cbuffer FrameConstants : register(b0)
        {
            float4x4 viewProjection;
            float4x4 lightViewProjection;
            float4 lightDirection;
            float4 cameraPosition;
            float4 flags;
            float4 roomLightPosition;
        };

        [[vk::binding(1, 0)]] Texture2D shadowMap : register(t0);
        [[vk::binding(2, 0)]] SamplerComparisonState shadowSampler : register(s0);

        struct PushConstants
        {
            float4x4 model;
            float4 albedo;       // rgb = base color, a = reflectivity (unused in this fallback)
            float4 objectFlags;  // x = useGroundPattern, y = useRoomLight
        };
        [[vk::push_constant]] PushConstants pc;

        struct PSInput
        {
            float4 position : SV_Position;
            float3 worldNormal : NORMAL;
            float2 uv : TEXCOORD0;
            float4 lightClipPos : TEXCOORD1;
            float3 worldPos : TEXCOORD2;
        };

        // Ground squares cycle through the RGB primaries — the basic colors production/compositing pipelines
        // are built on — tiled in 1-world-unit cells so the pattern reads clearly at this scene's scale.
        float3 GroundPatternColor(float2 xz)
        {
            int cellX = (int)floor(xz.x);
            int cellZ = (int)floor(xz.y);
            int idx = ((cellX + cellZ) % 3 + 3) % 3;
            if (idx == 0) return float3(0.85, 0.12, 0.12);
            if (idx == 1) return float3(0.10, 0.75, 0.20);
            return float3(0.15, 0.35, 0.95);
        }

        float SampleShadow(float4 lightClipPos)
        {
            float3 ndc = lightClipPos.xyz / lightClipPos.w;
            float2 shadowUv = ndc.xy * 0.5 + 0.5;
            if (shadowUv.x < 0.0 || shadowUv.x > 1.0 || shadowUv.y < 0.0 || shadowUv.y > 1.0)
                return 1.0;
            const float bias = 0.0015;
            return shadowMap.SampleCmp(shadowSampler, shadowUv, ndc.z - bias);
        }

        float4 main(PSInput input) : SV_Target
        {
            float3 normal = normalize(input.worldNormal);
            bool isRoomObject = pc.objectFlags.y > 0.5;

            float3 lightDir;
            float attenuation;
            float shadow;
            if (isRoomObject)
            {
                float3 toLight = roomLightPosition.xyz - input.worldPos;
                float dist = length(toLight);
                lightDir = toLight / max(dist, 0.0001);
                attenuation = 3.0 / (1.0 + 0.15 * dist + 0.05 * dist * dist);
                shadow = 1.0; // no shadow map exists for a moving point light without ray tracing
            }
            else
            {
                lightDir = normalize(lightDirection.xyz);
                attenuation = 1.0;
                shadow = SampleShadow(input.lightClipPos);
            }

            float ndotl = saturate(dot(normal, lightDir));
            float3 baseColor = (pc.objectFlags.x > 0.5) ? GroundPatternColor(input.worldPos.xz) : pc.albedo.rgb;
            float lighting = 0.25 + 0.75 * ndotl * shadow * attenuation;

            float3 viewDirToCamera = normalize(cameraPosition.xyz - input.worldPos);
            float3 halfVector = normalize(lightDir + viewDirToCamera);
            float specular = pow(saturate(dot(normal, halfVector)), 48.0) * 0.35 * shadow * attenuation;

            float3 color = baseColor * lighting + specular;
            color = color / (color + 1.0); // Reinhard tonemap so the specular highlight rolls off instead of clipping
            return float4(color, 1.0);
        }
        """;

    // ---- Ray-tracing-capable fragment shader. Shadow technique for the outdoor sun (map vs. real shadow ray)
    // is a runtime switch tied to the quality tier; the room's point light always uses real shadow rays (there's
    // no shadow map for something that moves every frame). Reflection and GI are material/geometry properties
    // that are always traced when available, independent of tier. Hits are shaded with a flat per-object color
    // rather than fully re-lit — that would need bindless per-instance vertex/index buffers to fetch the hit
    // triangle's data, which is a much bigger investment than this scene needs. ----

    public const string FragmentRT = """
        [[vk::binding(0, 0)]]
        cbuffer FrameConstants : register(b0)
        {
            float4x4 viewProjection;
            float4x4 lightViewProjection;
            float4 lightDirection;
            float4 cameraPosition;
            float4 flags; // x = useRayTracedShadows, y = per-frame noise seed, z = bounce sphere's reflectivity
            float4 roomLightPosition; // xyz = the room's moving point light position
        };

        [[vk::binding(1, 0)]] Texture2D shadowMap : register(t0);
        [[vk::binding(2, 0)]] SamplerComparisonState shadowSampler : register(s0);
        [[vk::binding(3, 0)]] RaytracingAccelerationStructure scene : register(t1);

        struct PushConstants
        {
            float4x4 model;
            float4 albedo;       // rgb = base/matte color, a = reflectivity (0..1)
            float4 objectFlags;  // x = useGroundPattern, y = useRoomLight
        };
        [[vk::push_constant]] PushConstants pc;

        struct PSInput
        {
            float4 position : SV_Position;
            float3 worldNormal : NORMAL;
            float2 uv : TEXCOORD0;
            float4 lightClipPos : TEXCOORD1;
            float3 worldPos : TEXCOORD2;
        };

        // instanceId matches the InstanceCustomIndex each TLAS instance was built with (Program.cs):
        // 0 = cube, 1 = ground, 2 = mirror sphere, 3 = bounce sphere (outdoor scene);
        // 4 = room floor, 5 = room ceiling, 6 = room back wall, 7 = room right wall, 8 = room mirror wall,
        // 9 = room cone, 10 = room sphere, 11 = room cube (indoor room — instanceId >= 4).
        static const float3 kCubeColor = float3(1.0, 1.0, 1.0);
        static const float3 kMirrorFallbackColor = float3(0.8, 0.8, 0.85);
        static const float3 kBounceMatteColor = float3(1.0, 0.45, 0.1);
        static const float3 kRoomFloorColor = float3(0.15, 0.75, 0.25);
        static const float3 kRoomCeilingColor = float3(0.92, 0.92, 0.92);
        static const float3 kRoomBackWallColor = float3(0.80, 0.15, 0.15);
        static const float3 kRoomRightWallColor = float3(0.20, 0.35, 0.90);
        static const float3 kRoomConeColor = float3(0.65, 0.30, 0.85);
        static const float3 kRoomSphereColor = float3(0.95, 0.80, 0.15);
        static const float3 kRoomCubeColor = float3(0.20, 0.85, 0.80);
        // Sphere/wall world positions — none of these ever move (only their materials do), so hit-shading can
        // compute an analytic normal for a second reflection bounce without needing bindless vertex data.
        static const float3 kMirrorSphereCenter = float3(-1.7, 0.6, 0.0);
        static const float3 kBounceSphereCenter = float3(1.7, 0.6, 0.0);
        static const float3 kMirrorWallNormal = float3(0.0, 0.0, 1.0);

        float3 GroundPatternColor(float2 xz)
        {
            int cellX = (int)floor(xz.x);
            int cellZ = (int)floor(xz.y);
            int idx = ((cellX + cellZ) % 3 + 3) % 3;
            if (idx == 0) return float3(0.85, 0.12, 0.12);
            if (idx == 1) return float3(0.10, 0.75, 0.20);
            return float3(0.15, 0.35, 0.95);
        }

        float SampleShadow(float4 lightClipPos)
        {
            float3 ndc = lightClipPos.xyz / lightClipPos.w;
            float2 shadowUv = ndc.xy * 0.5 + 0.5;
            if (shadowUv.x < 0.0 || shadowUv.x > 1.0 || shadowUv.y < 0.0 || shadowUv.y > 1.0)
                return 1.0;
            const float bias = 0.0015;
            return shadowMap.SampleCmp(shadowSampler, shadowUv, ndc.z - bias);
        }

        // Cheap per-pixel hash (Inigo-Quilez-style) — decorrelates sample directions between neighboring
        // pixels/rays; a `seed` that changes every frame turns static dither into moving grain.
        float Hash12(float2 p)
        {
            float3 p3 = frac(float3(p.xyx) * 0.1031);
            p3 += dot(p3, p3.yzx + 33.33);
            return frac((p3.x + p3.y) * p3.z);
        }

        float3 CosineSampleHemisphere(float3 normal, float2 xi)
        {
            float r = sqrt(xi.x);
            float phi = 6.2831853 * xi.y;
            float3 up = abs(normal.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0);
            float3 tangent = normalize(cross(up, normal));
            float3 bitangent = cross(normal, tangent);
            float3 dir = tangent * (r * cos(phi)) + bitangent * (r * sin(phi)) + normal * sqrt(max(0.0, 1.0 - xi.x));
            return normalize(dir);
        }

        // Picks which light illuminates a given ray-traced hit point: the room's moving point light for indoor
        // instances, the outdoor directional sun for everything else. Returns the direction to trace toward and
        // the max ray distance (the point light's actual distance, so nothing "behind" it wrongly occludes it;
        // effectively infinite for the directional sun).
        float3 HitLightDirection(float3 hitPos, uint instanceId, out float maxDist)
        {
            if (instanceId >= 4)
            {
                float3 toLight = roomLightPosition.xyz - hitPos;
                maxDist = length(toLight);
                return toLight / max(maxDist, 0.0001);
            }
            maxDist = 50.0;
            return normalize(lightDirection.xyz);
        }

        // Percentage-closer soft shadow, PCSS-style: first search a small cluster of rays around the light
        // direction to find the *average* occluder distance (steadier than trusting a single ray right at a
        // penumbra's noisy edge), then derive a penumbra radius from it and average several rays jittered
        // within a cone of that size. `lightDistance` is 0 for the (effectively infinitely far) directional
        // sun — its apparent size is constant, so the penumbra just grows linearly with how far the occluder
        // sits from the receiver: sharp right at a contact point, soft further along the ground. For the
        // room's point light, `lightDistance` is its real distance and the penumbra instead follows the
        // proper similar-triangles PCSS formula, which also grows the closer the occluder sits to the light
        // itself — an object almost touching the light casts a soft shadow everywhere, while one almost
        // touching the receiver casts a sharp one at its base — matching how real area/point lights behave,
        // not just a single hard edge everywhere.
        float TraceShadowRay(float3 origin, float3 lightDir, float2 seed, float maxDistance, float lightDistance)
        {
            float3 up = abs(lightDir.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0);
            float3 tangent = normalize(cross(up, lightDir));
            float3 bitangent = cross(lightDir, tangent);

            const float kSearchAngularRadius = 0.05;
            const int kBlockerSearchSamples = 4;
            float blockerDistSum = 0.0;
            int blockerHits = 0;
            for (int i = 0; i < kBlockerSearchSamples; i++)
            {
                // Sample 0 always tests the exact light direction (equivalent to the old single primary ray);
                // the rest jitter within a narrow cone to steady the average against a single unlucky miss.
                float3 searchDir = lightDir;
                if (i > 0)
                {
                    float2 xi = float2(Hash12(seed + float2(i * 41.41, 3.0)), Hash12(seed + float2(3.0, i * 53.53)));
                    float angle = xi.x * 6.2831853;
                    float radius = sqrt(xi.y) * kSearchAngularRadius;
                    searchDir = normalize(lightDir + (tangent * cos(angle) + bitangent * sin(angle)) * radius);
                }

                RayDesc searchRay;
                searchRay.Origin = origin;
                searchRay.Direction = searchDir;
                searchRay.TMin = 0.01;
                searchRay.TMax = maxDistance;

                RayQuery<RAY_FLAG_FORCE_OPAQUE | RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH | RAY_FLAG_SKIP_PROCEDURAL_PRIMITIVES> sq;
                sq.TraceRayInline(scene, RAY_FLAG_NONE, 0xFF, searchRay);
                sq.Proceed();
                if (sq.CommittedStatus() == COMMITTED_TRIANGLE_HIT)
                {
                    blockerDistSum += sq.CommittedRayT();
                    blockerHits++;
                }
            }

            if (blockerHits == 0)
                return 1.0; // no occluder found near the light direction at all — fully lit

            float avgBlockerDist = blockerDistSum / blockerHits;

            const float kSunAngularRadius = 0.08;
            const float kRoomLightRadius = 0.35;
            float penumbraRadius = (lightDistance <= 0.0)
                ? avgBlockerDist * kSunAngularRadius
                : kRoomLightRadius * avgBlockerDist / max(lightDistance - avgBlockerDist, 0.05);

            const int kSoftSamples = 8;
            float lit = 0.0;
            for (int s = 0; s < kSoftSamples; s++)
            {
                float2 xi = float2(Hash12(seed + float2(s * 17.17, 11.0)), Hash12(seed + float2(11.0, s * 29.29)));
                float angle = xi.x * 6.2831853;
                float radius = sqrt(xi.y) * penumbraRadius;
                float3 jitteredDir = normalize(lightDir + (tangent * cos(angle) + bitangent * sin(angle)) * radius);

                RayDesc ray;
                ray.Origin = origin;
                ray.Direction = jitteredDir;
                ray.TMin = 0.01;
                ray.TMax = maxDistance;

                RayQuery<RAY_FLAG_FORCE_OPAQUE | RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH | RAY_FLAG_SKIP_PROCEDURAL_PRIMITIVES> q;
                q.TraceRayInline(scene, RAY_FLAG_NONE, 0xFF, ray);
                q.Proceed();
                lit += (q.CommittedStatus() == COMMITTED_TRIANGLE_HIT) ? 0.0 : 1.0;
            }
            return lit / kSoftSamples;
        }

        // Single-ray hard shadow test, no cone softening — used only inside the GI loop, where each of the
        // several hemisphere samples can't also afford a multi-ray soft-shadow query without the ray count
        // exploding. Keeps indirect light from "leaking" through a hit point that's actually in shadow itself.
        float TraceHardShadowRay(float3 origin, float3 lightDir, float maxDistance)
        {
            RayDesc ray;
            ray.Origin = origin;
            ray.Direction = lightDir;
            ray.TMin = 0.01;
            ray.TMax = maxDistance;

            RayQuery<RAY_FLAG_FORCE_OPAQUE | RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH | RAY_FLAG_SKIP_PROCEDURAL_PRIMITIVES> q;
            q.TraceRayInline(scene, RAY_FLAG_NONE, 0xFF, ray);
            q.Proceed();
            return (q.CommittedStatus() == COMMITTED_TRIANGLE_HIT) ? 0.0 : 1.0;
        }

        // Terminal shading: never traces further. Used for GI hits and for the second reflection bounce below,
        // so a hit on a reflective surface just shows its flat fallback color rather than bouncing again — HLSL
        // doesn't allow recursive functions, so depth is bounded by having distinct non-recursive functions
        // (this one, and the full TraceReflection below) rather than a runtime depth counter.
        float3 ShadeHitTerminal(uint instanceId, float3 hitWorldPos)
        {
            if (instanceId == 1) return GroundPatternColor(hitWorldPos.xz);
            if (instanceId == 0) return kCubeColor;
            if (instanceId == 3) return kBounceMatteColor;
            if (instanceId == 4) return kRoomFloorColor;
            if (instanceId == 5) return kRoomCeilingColor;
            if (instanceId == 6) return kRoomBackWallColor;
            if (instanceId == 7) return kRoomRightWallColor;
            if (instanceId == 9) return kRoomConeColor;
            if (instanceId == 10) return kRoomSphereColor;
            if (instanceId == 11) return kRoomCubeColor;
            return kMirrorFallbackColor; // instanceId == 2 (mirror sphere) or 8 (mirror wall)
        }

        float3 SkyColor(float3 dir)
        {
            float skyT = saturate(dir.y * 0.5 + 0.5);
            float3 sky = lerp(float3(0.05, 0.05, 0.09), float3(0.35, 0.55, 0.85), skyT);
            float sunAmount = pow(saturate(dot(dir, normalize(lightDirection.xyz))), 256.0);
            sky += float3(1.0, 0.9, 0.7) * sunAmount * 2.0;
            return sky;
        }

        // One cheap, un-shadowed hemisphere sample — used only to give a shadowed point *some* plausible bounce
        // color to inherit (see ShadowDimmingAt). Deliberately doesn't call ShadowDimmingAt itself: HLSL forbids
        // recursive functions, so this has to stay a strict dead end.
        float3 CheapBounceEstimate(float3 origin, float3 pseudoNormal, float2 seed)
        {
            float2 xi = float2(Hash12(seed), Hash12(seed + float2(11.0, 5.0)));
            float3 dir = CosineSampleHemisphere(pseudoNormal, xi);

            RayDesc ray;
            ray.Origin = origin;
            ray.Direction = dir;
            ray.TMin = 0.02;
            ray.TMax = 8.0;

            RayQuery<RAY_FLAG_FORCE_OPAQUE | RAY_FLAG_SKIP_PROCEDURAL_PRIMITIVES> q;
            q.TraceRayInline(scene, RAY_FLAG_NONE, 0xFF, ray);
            q.Proceed();

            if (q.CommittedStatus() == COMMITTED_TRIANGLE_HIT)
                return ShadeHitTerminal(q.CommittedInstanceID(), origin + dir * q.CommittedRayT());
            return SkyColor(dir);
        }

        // How a ray-traced hit point should look if it's in shadow: not a flat gray dim, but a tint toward
        // whatever's actually bouncing light around it right there (floored so it never goes fully black) —
        // e.g. a shadow on the green floor near the red wall picks up a reddish tint, the same idea as the main
        // direct-shading path's GI term, just a single cheap sample since this runs deep inside reflection
        // tracing where the ray budget is already stacked up. `pseudoNormal` doesn't need to be exact — the
        // incoming ray's negated direction is close enough for this secondary, already-approximate estimate.
        float3 ShadowDimmingAt(float3 hitPos, float3 pseudoNormal, uint instanceId, float2 seed)
        {
            float maxDist;
            float3 lightDir = HitLightDirection(hitPos, instanceId, maxDist);
            float lightDistance = (instanceId >= 4) ? maxDist : 0.0;
            float hitShadow = TraceShadowRay(hitPos + lightDir * 0.02, lightDir, seed, maxDist, lightDistance);

            if (hitShadow > 0.999)
                return float3(1.0, 1.0, 1.0);

            float3 bounce = CheapBounceEstimate(hitPos + pseudoNormal * 0.02, pseudoNormal, seed);
            float3 shadowedMultiplier = max(bounce, float3(0.25, 0.25, 0.25));
            return lerp(shadowedMultiplier, float3(1.0, 1.0, 1.0), hitShadow);
        }

        float3 TraceRayTerminal(float3 origin, float3 dir, float maxT)
        {
            RayDesc ray;
            ray.Origin = origin;
            ray.Direction = dir;
            ray.TMin = 0.02;
            ray.TMax = maxT;

            RayQuery<RAY_FLAG_FORCE_OPAQUE | RAY_FLAG_SKIP_PROCEDURAL_PRIMITIVES> q;
            q.TraceRayInline(scene, RAY_FLAG_NONE, 0xFF, ray);
            q.Proceed();

            if (q.CommittedStatus() != COMMITTED_TRIANGLE_HIT)
                return SkyColor(dir);

            float3 hitPos = origin + dir * q.CommittedRayT();
            uint instanceId = q.CommittedInstanceID();
            float3 hitColor = ShadeHitTerminal(instanceId, hitPos);
            float2 seed = hitPos.xz * 7.0 + flags.yy;
            return hitColor * ShadowDimmingAt(hitPos, -dir, instanceId, seed);
        }

        // Primary reflection trace. Flat-shaded geometry (ground/cube/room walls & objects) is always accurate
        // (pure functions of position/constants). The mirror sphere, bounce sphere, and mirror wall are also
        // reflective, so a ray that hits one of them shows its *current* appearance — including the bounce
        // sphere's live oscillating reflectivity (flags.z, updated every frame in Program.cs) — via one bounded
        // second bounce (TraceRayTerminal), instead of a frozen placeholder. That second bounce never reflects
        // further, keeping the ray budget fixed at 2 per pixel. Every hit also gets its own real shadow ray
        // (ShadowDimmingAt), so a reflected patch that's actually in shadow shows up dim (and colored) too.
        float3 TraceReflection(float3 origin, float3 dir)
        {
            RayDesc ray;
            ray.Origin = origin;
            ray.Direction = dir;
            ray.TMin = 0.02;
            ray.TMax = 50.0;

            RayQuery<RAY_FLAG_FORCE_OPAQUE | RAY_FLAG_SKIP_PROCEDURAL_PRIMITIVES> q;
            q.TraceRayInline(scene, RAY_FLAG_NONE, 0xFF, ray);
            q.Proceed();

            if (q.CommittedStatus() != COMMITTED_TRIANGLE_HIT)
                return SkyColor(dir);

            float t = q.CommittedRayT();
            uint instanceId = q.CommittedInstanceID();
            float3 hitPos = origin + dir * t;
            float fog = lerp(0.5, 1.0, saturate(1.0 - t / 40.0));

            float3 hitColor;
            if (instanceId == 1) hitColor = GroundPatternColor(hitPos.xz);
            else if (instanceId == 0) hitColor = kCubeColor;
            else if (instanceId == 4) hitColor = kRoomFloorColor;
            else if (instanceId == 5) hitColor = kRoomCeilingColor;
            else if (instanceId == 6) hitColor = kRoomBackWallColor;
            else if (instanceId == 7) hitColor = kRoomRightWallColor;
            else if (instanceId == 9) hitColor = kRoomConeColor;
            else if (instanceId == 10) hitColor = kRoomSphereColor;
            else if (instanceId == 11) hitColor = kRoomCubeColor;
            else
            {
                // instanceId == 2 (mirror sphere), 3 (bounce sphere), or 8 (mirror wall) — all reflective.
                float3 hitNormal;
                float hitReflectivity;
                float3 matteColor;
                if (instanceId == 2)
                {
                    hitNormal = normalize(hitPos - kMirrorSphereCenter);
                    hitReflectivity = 1.0;
                    matteColor = kMirrorFallbackColor;
                }
                else if (instanceId == 3)
                {
                    hitNormal = normalize(hitPos - kBounceSphereCenter);
                    hitReflectivity = flags.z;
                    matteColor = kBounceMatteColor;
                }
                else
                {
                    hitNormal = kMirrorWallNormal;
                    hitReflectivity = 1.0;
                    matteColor = kMirrorFallbackColor;
                }

                if (hitReflectivity > 0.001)
                {
                    float3 bounceDir = reflect(dir, hitNormal);
                    float3 secondBounceColor = TraceRayTerminal(hitPos + hitNormal * 0.02, bounceDir, 40.0);
                    hitColor = lerp(matteColor, secondBounceColor, hitReflectivity);
                }
                else
                {
                    hitColor = matteColor;
                }
            }

            float2 seed = hitPos.xz * 13.37 + flags.yy;
            hitColor *= ShadowDimmingAt(hitPos, -dir, instanceId, seed);
            return hitColor * fog;
        }

        // Multi-bounce ray-traced global illumination: for each of a handful of cosine-weighted hemisphere
        // samples, walk up to kMaxBounces path segments in a loop (not recursion — HLSL forbids that), so
        // light genuinely bounces more than once — e.g. the room's red wall tints the floor, and the floor's
        // now-reddened bounce tints a sphere two bounces away, instead of stopping after one hop. Each hit
        // contributes its own shadow-tested direct light, scaled by how much energy survived every earlier
        // bounce's surface albedo (`throughput`). Hits reuse the same flat per-instance ShadeHitTerminal()
        // colors as reflection/shadow tinting elsewhere (no bindless vertex fetch for a real hit normal), and
        // the path continues using the incoming ray's negated direction as a stand-in normal — the same
        // "pseudoNormal" approximation ShadowDimmingAt already relies on.
        float3 TraceIndirectLight(float3 origin, float3 normal, float2 seed)
        {
            const int kSamples = 4;
            const int kMaxBounces = 2;
            float3 accum = float3(0, 0, 0);
            for (int i = 0; i < kSamples; i++)
            {
                float3 rayOrigin = origin;
                float3 rayNormal = normal;
                float3 throughput = float3(1.0, 1.0, 1.0);
                float3 sampleColor = float3(0, 0, 0);
                float2 sampleSeed = seed + float2(i * 13.13, i * 7.77);

                for (int bounce = 0; bounce < kMaxBounces; bounce++)
                {
                    float2 xi = float2(Hash12(sampleSeed), Hash12(sampleSeed + float2(3.33, 9.91)));
                    sampleSeed += float2(19.19, 27.27);
                    float3 dir = CosineSampleHemisphere(rayNormal, xi);

                    RayDesc ray;
                    ray.Origin = rayOrigin;
                    ray.Direction = dir;
                    ray.TMin = 0.02;
                    ray.TMax = 8.0; // indirect light is short-range here; keeps traversal cheap too

                    RayQuery<RAY_FLAG_FORCE_OPAQUE | RAY_FLAG_SKIP_PROCEDURAL_PRIMITIVES> q;
                    q.TraceRayInline(scene, RAY_FLAG_NONE, 0xFF, ray);
                    q.Proceed();

                    if (q.CommittedStatus() != COMMITTED_TRIANGLE_HIT)
                    {
                        sampleColor += throughput * SkyColor(dir);
                        break;
                    }

                    float t = q.CommittedRayT();
                    float3 hitPos = rayOrigin + dir * t;
                    uint instId = q.CommittedInstanceID();
                    float3 hitAlbedo = ShadeHitTerminal(instId, hitPos);

                    // Terminal per-hit shading: a cheap hard shadow test so a hit point that's itself in
                    // shadow doesn't bounce full-brightness light back (no light leaking through shadows).
                    float maxDist;
                    float3 hitLightDir = HitLightDirection(hitPos, instId, maxDist);
                    float hitShadow = TraceHardShadowRay(hitPos + hitLightDir * 0.02, hitLightDir, maxDist);
                    float distFade = saturate(1.0 - t / 8.0);

                    sampleColor += throughput * hitAlbedo * lerp(0.3, 1.0, hitShadow) * distFade;

                    throughput *= hitAlbedo * 0.6; // energy lost to the surface's own reflectance each bounce
                    rayNormal = -dir;
                    rayOrigin = hitPos + rayNormal * 0.02;
                }
                accum += sampleColor;
            }
            return accum / kSamples;
        }

        float4 main(PSInput input) : SV_Target
        {
            float3 normal = normalize(input.worldNormal);
            bool isRoomObject = pc.objectFlags.y > 0.5;

            float3 lightDir;
            float attenuation;
            float shadow;
            if (isRoomObject)
            {
                float3 toLight = roomLightPosition.xyz - input.worldPos;
                float dist = length(toLight);
                lightDir = toLight / max(dist, 0.0001);
                attenuation = 3.0 / (1.0 + 0.15 * dist + 0.05 * dist * dist);
                // No shadow map exists for a moving point light — always a real shadow ray here.
                shadow = TraceShadowRay(input.worldPos + normal * 0.02, lightDir, input.position.xy + flags.yy + float2(41.0, 17.0), dist, dist);
            }
            else
            {
                lightDir = normalize(lightDirection.xyz);
                attenuation = 1.0;
                shadow = (flags.x > 0.5)
                    ? TraceShadowRay(input.worldPos + normal * 0.02, lightDir, input.position.xy + flags.yy + float2(41.0, 17.0), 50.0, 0.0)
                    : SampleShadow(input.lightClipPos);
            }

            float ndotl = saturate(dot(normal, lightDir));
            float3 baseColor = (pc.objectFlags.x > 0.5) ? GroundPatternColor(input.worldPos.xz) : pc.albedo.rgb;

            float3 bounceOrigin = input.worldPos + normal * 0.02;
            float3 indirect = TraceIndirectLight(bounceOrigin, normal, input.position.xy + flags.yy) * 0.6;
            float3 direct = float3(0.75, 0.75, 0.75) * ndotl * shadow * attenuation;
            float3 diffuseColor = baseColor * (indirect + direct);

            float3 viewDirFromCamera = normalize(input.worldPos - cameraPosition.xyz);
            float3 viewDirToCamera = -viewDirFromCamera;

            // Cheap Blinn-Phong sheen — pure Lambertian + a hard shadow term reads as flat/plastic-y; a small
            // view-dependent highlight (dimmed by the same shadow so it vanishes in shade) sells "lit by an
            // actual light" much better than diffuse alone, for negligible cost (no extra rays).
            float3 halfVector = normalize(lightDir + viewDirToCamera);
            float specular = pow(saturate(dot(normal, halfVector)), 48.0) * 0.35 * shadow * attenuation;

            float reflectivity = pc.albedo.a;
            // Fresnel (Schlick-style): real reflective materials get noticeably more mirror-like at grazing
            // angles. Gated on the object's own base reflectivity so non-reflective objects stay exactly as
            // matte as before — this only adds a rim reflection to already-reflective surfaces.
            float fresnel = pow(1.0 - saturate(dot(normal, viewDirToCamera)), 5.0);
            float effectiveReflectivity = reflectivity + (1.0 - reflectivity) * fresnel * (reflectivity > 0.001 ? 1.0 : 0.0);

            float3 surfaceColor = diffuseColor;
            if (effectiveReflectivity > 0.001)
            {
                float3 reflectedDir = reflect(viewDirFromCamera, normal);
                float3 reflColor = TraceReflection(bounceOrigin, reflectedDir);
                surfaceColor = lerp(diffuseColor, reflColor, effectiveReflectivity);
            }

            surfaceColor += specular;

            // Reinhard tonemap: the specular term can push values over 1.0, and without this they'd clip to a
            // harsh flat-white disc instead of rolling off smoothly.
            surfaceColor = surfaceColor / (surfaceColor + 1.0);

            return float4(surfaceColor, 1.0);
        }
        """;

    // ---- Shadow pass: depth-only, rendered from the light's point of view ----

    public const string ShadowVertex = """
        struct PushConstants
        {
            float4x4 lightMvp;
        };
        [[vk::push_constant]] PushConstants pc;

        struct VSInput
        {
            float3 position : POSITION;
            float3 normal : NORMAL;
            float2 uv : TEXCOORD0;
        };

        struct VSOutput
        {
            float4 position : SV_Position;
        };

        VSOutput main(VSInput input)
        {
            VSOutput output;
            output.position = mul(float4(input.position, 1.0), pc.lightMvp);
            return output;
        }
        """;

    public const string ShadowFragment = """
        struct PSInput
        {
            float4 position : SV_Position;
        };

        void main(PSInput input)
        {
        }
        """;

    // ---- UI overlay: unlit textured quad in screen space, alpha blended ----

    public const string UiVertex = """
        struct VSInput
        {
            float2 position : POSITION;
            float2 uv : TEXCOORD0;
        };

        struct VSOutput
        {
            float4 position : SV_Position;
            float2 uv : TEXCOORD0;
        };

        VSOutput main(VSInput input)
        {
            VSOutput output;
            output.position = float4(input.position, 0.0, 1.0);
            output.uv = input.uv;
            return output;
        }
        """;

    public const string UiFragment = """
        [[vk::binding(0, 0)]] Texture2D tex : register(t0);
        [[vk::binding(1, 0)]] SamplerState samp : register(s0);

        struct PSInput
        {
            float4 position : SV_Position;
            float2 uv : TEXCOORD0;
        };

        float4 main(PSInput input) : SV_Target
        {
            return tex.Sample(samp, input.uv);
        }
        """;
}
