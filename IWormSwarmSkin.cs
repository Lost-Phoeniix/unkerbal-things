using System;
using UnityEngine;

/// <summary>What EntityTracker and the test probe need from a worm-swarm body: MohoWormGpuSkin (mohoWormBody = gpu) or its fallback MohoWormHeapSkin.</summary>
public interface IWormSwarmSkin : IMohoSkin, IDisposable
{
	void Initialize(CelestialBody celestialBody, Vector3 ground, Func<Vector3, Vector3> sampler);
	void SetGroundSampler(Func<Vector3, Vector3> sampler);
	void SetTarget(Vector3d worldTarget, bool pursue);
	void ApplyWorldShift(Vector3 delta);
	void Reposition(Vector3 shift);
	string BodyName { get; }
	int WormTotal { get; }
	int PointCount { get; }
	string ShaderName { get; }
	int RenderedTriangles { get; }
	bool RendererEnabled { get; }
	Bounds RenderBounds { get; }
	Vector3 Anchor { get; }
	Quaternion Frame { get; }
	Vector3 PointCentroid { get; }
	int SpilledWorms { get; }
	float MaxPointRadius { get; }
	float MaxSegmentLength { get; }
	float OverlapFraction { get; }
	float RearDragFraction { get; }
	float PileHeight { get; }
	int TumbleCount { get; }
	float HeadingCoherence { get; }
	float HeapAspect { get; }
	float LastApplyMilliseconds { get; }
	float LastTerrainMilliseconds { get; }
	float LastUploadMilliseconds { get; }
	float LastSimulationMilliseconds { get; }
	float LastMeshMilliseconds { get; }
	int WorkerSteps { get; }
	bool Threaded { get; }
}
