using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using NaughtyAttributes;

/// <summary>
/// Place on any GameObject with a trigger Collider.
/// When a MovingCar enters the trigger it receives a velocity impulse,
/// or (optional) follows a simulated ballistic arc with input locked.
/// </summary>
public class BoostPad : MonoBehaviour {

	struct TrajectorySample {
		public Vector3 position;
		public Vector3 velocity;
		public float   time;
	}

	struct TrajectoryResult {
		public List<TrajectorySample> samples;
		public float   totalTime;
		public bool    hitGround;
		public Vector3 landingPoint;
		public Vector3 landingNormal;
	}

	// ── Boost ─────────────────────────────────────────────────────────

	[BoxGroup("Boost"), SerializeField, Range(0f, 100f),
	 Tooltip("Speed added to the car in the boost direction (m/s).")]
	float boostSpeed = 25f;

	[BoxGroup("Boost"), SerializeField,
	 Tooltip("Car Forward  — launches the car along its own nose.\n" +
	         "Pad Forward  — launches along this trigger's local +Z.\n" +
	         "Gravity Up   — launches straight up away from the planet surface.")]
	BoostDirection direction = BoostDirection.CarForward;

	[BoxGroup("Boost"), SerializeField, Range(0f, 1f),
	 Tooltip("0 = replaces existing velocity in the boost axis (clean launch).\n" +
	         "1 = adds on top of whatever the car already has (feels snappier).")]
	float additive = 0.5f;

	// ── Cooldown ──────────────────────────────────────────────────────

	[BoxGroup("Cooldown"), SerializeField, Min(0f),
	 Tooltip("Seconds before this pad can boost again. 0 = instant re-trigger.")]
	float cooldown = 1.5f;

	[BoxGroup("Cooldown"), SerializeField,
	 Tooltip("If true the pad can only be used once per play session.")]
	bool oneShot = false;

	// ── Trajectory Preview ────────────────────────────────────────────

	[BoxGroup("Trajectory"), SerializeField,
	 Tooltip("When enabled the car follows the simulated arc to landing with steering disabled for the whole flight.")]
	bool usePredefinedTrajectory = false;

	[BoxGroup("Trajectory"), SerializeField]
	bool showTrajectory = true;

	[BoxGroup("Trajectory"), SerializeField, Range(20, 300),
	 Tooltip("How many simulation steps to draw. More = longer arc but slower to redraw.")]
	int trajectorySteps = 120;

	[BoxGroup("Trajectory"), SerializeField, Range(0.01f, 0.15f),
	 Tooltip("Physics time step per simulation tick (seconds). Smaller = more accurate curve.")]
	float trajectoryTimeStep = 0.04f;

	[BoxGroup("Trajectory"), SerializeField,
	 Tooltip("Which layers the predicted arc can land on. Match your ground layers.")]
	LayerMask trajectoryMask = -1;

	[BoxGroup("Trajectory"), SerializeField]
	Color trajectoryColor = new Color(1f, 0.85f, 0.1f, 0.9f);

	// ── Visual Feedback ───────────────────────────────────────────────

	[BoxGroup("Visual"), SerializeField,
	 Tooltip("Optional renderer whose material colour will pulse on boost.")]
	Renderer padRenderer;

	[BoxGroup("Visual"), SerializeField]
	Color activeColor = new Color(1f, 0.85f, 0.1f);

	[BoxGroup("Visual"), SerializeField]
	Color cooldownColor = new Color(0.25f, 0.25f, 0.25f);

	[BoxGroup("Visual"), SerializeField, Min(0f),
	 Tooltip("Seconds the pad glows at activeColor before fading back.")]
	float glowDuration = 0.4f;

	// ── Runtime ───────────────────────────────────────────────────────

	float _lastBoostTime = -999f;
	bool  _spent         = false;   // for oneShot
	Color _baseColor;
	Coroutine _followRoutine;
	MovingCar _followingCar;
	static readonly int ColorProp = Shader.PropertyToID("_BaseColor");

	// ─────────────────────────────────────────────────────────────────

	enum BoostDirection { CarForward, PadForward, GravityUp }

	// ─────────────────────────────────────────────────────────────────

	void Awake () {
		if (padRenderer != null)
			_baseColor = padRenderer.material.HasProperty(ColorProp)
				? padRenderer.material.GetColor(ColorProp)
				: padRenderer.material.color;
	}

	void OnDisable () => CancelTrajectoryFollow();

	void CancelTrajectoryFollow () {
		if (_followRoutine != null) {
			StopCoroutine(_followRoutine);
			_followRoutine = null;
		}
		if (_followingCar != null && _followingCar.IsTrajectoryLocked)
			_followingCar.SetTrajectoryLocked(false);
		_followingCar = null;
	}

	void OnTriggerEnter (Collider other) {
		if (_spent) return;
		if (Time.time - _lastBoostTime < cooldown) return;
		if (_followRoutine != null) return;

		var car = other.GetComponentInParent<MovingCar>();
		if (car == null) return;
		if (car.IsTrajectoryLocked) return;

		var rb = car.GetComponent<Rigidbody>();
		if (rb == null) return;

		if (usePredefinedTrajectory)
			StartPredefinedTrajectory(rb, car);
		else
			ApplyBoost(rb, car);

		_lastBoostTime = Time.time;
		if (oneShot) _spent = true;

		if (padRenderer != null)
			StartCoroutine(GlowRoutine());
	}

	void StartPredefinedTrajectory (Rigidbody rb, MovingCar car) {
		Vector3 boostDir = ResolveBoostDir(rb, car);
		Vector3 startVel = ComputeBoostVelocity(rb.linearVelocity, boostDir);

		TrajectoryResult result = SimulateTrajectory(rb.position, startVel);
		if (result.samples.Count < 2) {
			rb.linearVelocity = startVel;
			return;
		}

		if (_followRoutine != null)
			StopCoroutine(_followRoutine);

		_followingCar   = car;
		_followRoutine  = StartCoroutine(FollowTrajectoryRoutine(rb, car, result, startVel));
	}

	IEnumerator FollowTrajectoryRoutine (
		Rigidbody rb, MovingCar car, TrajectoryResult result, Vector3 launchVel)
	{
		car.SetTrajectoryLocked(true);

		float elapsed    = 0f;
		var   gravityCar = car.GetComponent<GravityCar>();

		// Snap to launch state so the arc matches the preview.
		rb.linearVelocity = launchVel;
		rb.MovePosition(result.samples[0].position);

		while (elapsed < result.totalTime) {
			elapsed += Time.fixedDeltaTime;

			TrajectorySample sample = SampleTrajectoryAt(result.samples, elapsed);
			rb.MovePosition(sample.position);
			rb.linearVelocity = sample.velocity;

			if (sample.velocity.sqrMagnitude > 0.25f && gravityCar != null) {
				Vector3 up = gravityCar.UpAxis.sqrMagnitude > 0.001f
					? gravityCar.UpAxis
					: Vector3.up;
				Quaternion look = Quaternion.LookRotation(sample.velocity.normalized, up);
				rb.MoveRotation(look);
			}

			yield return new WaitForFixedUpdate();
		}

		TrajectorySample end = result.samples[result.samples.Count - 1];
		rb.linearVelocity = end.velocity;

		if (car.IsTrajectoryLocked)
			car.SetTrajectoryLocked(false);

		_followRoutine = null;
		_followingCar  = null;
	}

	static TrajectorySample SampleTrajectoryAt (List<TrajectorySample> samples, float time) {
		if (time <= samples[0].time) return samples[0];
		for (int i = 1; i < samples.Count; i++) {
			var a = samples[i - 1];
			var b = samples[i];
			if (time <= b.time) {
				float span = b.time - a.time;
				float t    = span > 0.0001f ? (time - a.time) / span : 1f;
				return new TrajectorySample {
					position = Vector3.Lerp(a.position, b.position, t),
					velocity = Vector3.Lerp(a.velocity, b.velocity, t),
					time     = time
				};
			}
		}
		return samples[samples.Count - 1];
	}

	void ApplyBoost (Rigidbody rb, MovingCar car) {
		Vector3 boostDir = ResolveBoostDir(rb, car);
		Vector3 impulse  = ComputeBoostVelocity(rb.linearVelocity, boostDir) - rb.linearVelocity;
		rb.AddForce(impulse, ForceMode.VelocityChange);
	}

	Vector3 ResolveBoostDir (Rigidbody rb, MovingCar car) {
		Vector3 boostDir;
		switch (direction) {
			case BoostDirection.PadForward:
				boostDir = transform.forward;
				break;
			case BoostDirection.GravityUp:
				boostDir = car.GetComponent<GravityCar>().UpAxis;
				break;
			default:
				boostDir = rb.transform.forward;
				break;
		}
		return boostDir.normalized;
	}

	// Returns the post-boost velocity (same math as the impulse, without AddForce).
	Vector3 ComputeBoostVelocity (Vector3 currentVel, Vector3 boostDir) {
		float existingComponent = Vector3.Dot(currentVel, boostDir);
		float targetComponent   = Mathf.Max(existingComponent, boostSpeed);
		float replaceImpulse    = targetComponent - existingComponent;
		float addImpulse        = boostSpeed;
		float impulse           = Mathf.Lerp(replaceImpulse, addImpulse, additive);
		return currentVel + boostDir * impulse;
	}

	TrajectoryResult SimulateTrajectory (Vector3 startPos, Vector3 startVel) {
		var result = new TrajectoryResult {
			samples   = new List<TrajectorySample>(trajectorySteps + 1),
			totalTime = 0f,
			hitGround = false
		};

		Vector3 pos = startPos;
		Vector3 vel = startVel;
		float   t   = 0f;

		result.samples.Add(new TrajectorySample { position = pos, velocity = vel, time = t });

		for (int i = 0; i < trajectorySteps; i++) {
			Vector3 gravity = CustomGravity.GetGravity(pos);
			if (gravity.sqrMagnitude < 0.001f) gravity = Physics.gravity;

			Vector3 nextVel = vel + gravity * trajectoryTimeStep;
			Vector3 nextPos = pos + vel * trajectoryTimeStep
			                      + gravity * (0.5f * trajectoryTimeStep * trajectoryTimeStep);
			t += trajectoryTimeStep;

			Vector3 step    = nextPos - pos;
			float   stepLen = step.magnitude;
			if (stepLen > 0.001f &&
			    Physics.Raycast(pos, step / stepLen, out RaycastHit hit,
			                    stepLen, trajectoryMask,
			                    QueryTriggerInteraction.Ignore))
			{
				float hitFrac = hit.distance / stepLen;
				result.samples.Add(new TrajectorySample {
					position = hit.point,
					velocity = Vector3.Lerp(vel, nextVel, hitFrac),
					time     = t - trajectoryTimeStep + trajectoryTimeStep * hitFrac
				});
				result.totalTime    = result.samples[result.samples.Count - 1].time;
				result.hitGround    = true;
				result.landingPoint = hit.point;
				result.landingNormal = hit.normal;
				return result;
			}

			result.samples.Add(new TrajectorySample {
				position = nextPos,
				velocity = nextVel,
				time     = t
			});

			pos = nextPos;
			vel = nextVel;
		}

		result.totalTime = t;
		return result;
	}

	// ─────────────────────────────────────────────────────────────────
	//  Visual
	// ─────────────────────────────────────────────────────────────────

	System.Collections.IEnumerator GlowRoutine () {
		SetPadColor(activeColor);
		yield return new WaitForSeconds(glowDuration);

		// Fade back to base (or cooldown colour if still in cooldown)
		float t = 0f;
		Color from = activeColor;
		Color to   = _spent ? cooldownColor : _baseColor;
		while (t < 1f) {
			t += Time.deltaTime / Mathf.Max(glowDuration, 0.05f);
			SetPadColor(Color.Lerp(from, to, t));
			yield return null;
		}

		// Stay dimmed for the rest of the cooldown, then restore
		if (!_spent && cooldown > glowDuration) {
			SetPadColor(cooldownColor);
			yield return new WaitForSeconds(cooldown - glowDuration);
			SetPadColor(_baseColor);
		}
	}

	void SetPadColor (Color c) {
		if (padRenderer == null) return;
		if (padRenderer.material.HasProperty(ColorProp))
			padRenderer.material.SetColor(ColorProp, c);
		else
			padRenderer.material.color = c;
	}

#if UNITY_EDITOR
	// ── Always-visible indicator ──────────────────────────────────────
	void OnDrawGizmos () {
		Vector3 origin = transform.position;
		Vector3 dir    = PreviewBoostDir();
		bool    ready  = !_spent && (Time.time - _lastBoostTime >= cooldown);

		Color readyCol = usePredefinedTrajectory
			? new Color(0.2f, 0.85f, 1f, 0.85f)
			: new Color(1f, 0.85f, 0.1f, 0.85f);
		Gizmos.color = ready ? readyCol : new Color(0.4f, 0.4f, 0.4f, 0.5f);
		Gizmos.DrawLine(origin, origin + dir * boostSpeed * 0.1f);
		Gizmos.DrawSphere(origin + dir * boostSpeed * 0.1f, 0.12f);
		Gizmos.color = new Color(1f, 0.85f, 0.1f, 0.12f);
		Gizmos.DrawSphere(origin, 0.5f);
	}

	// ── Full trajectory when selected ─────────────────────────────────
	void OnDrawGizmosSelected () {
		if (!showTrajectory) return;

		Vector3 origin   = transform.position;
		Vector3 boostDir = PreviewBoostDir();
		Vector3 startVel = boostDir * boostSpeed;

		TrajectoryResult result = SimulateTrajectory(origin, startVel);
		DrawTrajectoryGizmo(result, origin, boostDir);
	}

	void DrawTrajectoryGizmo (TrajectoryResult result, Vector3 origin, Vector3 boostDir) {
		var samples = result.samples;
		if (samples.Count < 2) return;

		Color pathColor = usePredefinedTrajectory
			? new Color(0.2f, 0.85f, 1f, trajectoryColor.a)
			: trajectoryColor;

		for (int i = 1; i < samples.Count; i++) {
			float frac = (float)i / (samples.Count - 1);
			var   a    = samples[i - 1];
			var   b    = samples[i];

			Color c = TrajectorySegmentColor(frac, pathColor);
			UnityEditor.Handles.color = c;
			UnityEditor.Handles.DrawLine(a.position, b.position, usePredefinedTrajectory ? 3.5f : 2.5f);

			if (i % 6 == 0) {
				float dotSize = Mathf.Lerp(0.10f, 0.03f, frac);
				Gizmos.color  = c;
				Gizmos.DrawSphere(b.position, dotSize);
			}

			float labelInterval = 0.5f;
			float prevT = a.time;
			if (Mathf.Floor(b.time / labelInterval) > Mathf.Floor(prevT / labelInterval)) {
				UnityEditor.Handles.Label(b.position,
					$" {b.time:F1}s",
					new GUIStyle(GUI.skin.label) {
						normal    = { textColor = new Color(1f, 0.95f, 0.4f, 0.85f) },
						fontSize  = 9,
						fontStyle = FontStyle.Bold
					});
			}

		}

		if (result.hitGround) {
			UnityEditor.Handles.color = new Color(1f, 0.3f, 0.1f, 0.85f);
			UnityEditor.Handles.DrawWireDisc(result.landingPoint + result.landingNormal * 0.02f,
			                                  result.landingNormal, 0.45f);
			UnityEditor.Handles.DrawWireDisc(result.landingPoint + result.landingNormal * 0.02f,
			                                  result.landingNormal, 0.25f);
			Gizmos.color = new Color(1f, 0.3f, 0.1f, 0.7f);
			Gizmos.DrawSphere(result.landingPoint, 0.12f);

			UnityEditor.Handles.Label(
				result.landingPoint + result.landingNormal * 0.6f,
				$" Land  {result.totalTime:F2}s",
				new GUIStyle(GUI.skin.label) {
					normal    = { textColor = new Color(1f, 0.4f, 0.2f, 1f) },
					fontSize  = 10,
					fontStyle = FontStyle.Bold
				});
		}

		string modeNote = direction == BoostDirection.CarForward
			? "(CarForward — preview uses Pad Forward)"
			: direction.ToString();
		string trajNote = usePredefinedTrajectory ? "  [Scripted]" : "";
		UnityEditor.Handles.Label(origin + boostDir * 0.6f,
			$" {boostSpeed:F0} m/s  [{modeNote}]{trajNote}",
			new GUIStyle(GUI.skin.label) {
				normal    = { textColor = pathColor },
				fontSize  = 9
			});
	}

	// Boost direction the preview can resolve without a live car.
	// CarForward falls back to Pad Forward since no car exists in the editor.
	Vector3 PreviewBoostDir () {
		switch (direction) {
			case BoostDirection.PadForward:
				return transform.forward;
			case BoostDirection.GravityUp: {
				Vector3 g = CustomGravity.GetGravity(transform.position);
				if (g.sqrMagnitude < 0.001f) g = Physics.gravity;
				return -g.normalized;
			}
			default: // CarForward — use pad forward as the editor stand-in
				return transform.forward;
		}
	}

	Color TrajectorySegmentColor (float frac, Color baseColor) {
		return new Color(
			baseColor.r,
			baseColor.g,
			baseColor.b,
			baseColor.a * Mathf.Lerp(1f, 0.15f, frac));
	}
#endif
}
