using UnityEngine;
using NaughtyAttributes;

/// <summary>
/// Place on any GameObject with a trigger Collider.
/// When a MovingCar enters the trigger it receives a velocity impulse.
/// </summary>
public class BoostPad : MonoBehaviour {

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

	void OnTriggerEnter (Collider other) {
		if (_spent) return;
		if (Time.time - _lastBoostTime < cooldown) return;

		var car = other.GetComponentInParent<MovingCar>();
		if (car == null) return;

		var rb = car.GetComponent<Rigidbody>();
		if (rb == null) return;

		ApplyBoost(rb, car);
		_lastBoostTime = Time.time;
		if (oneShot) _spent = true;

		if (padRenderer != null)
			StartCoroutine(GlowRoutine());
	}

	void ApplyBoost (Rigidbody rb, MovingCar car) {
		// ── Resolve the boost direction ───────────────────────────────
		Vector3 boostDir;
		switch (direction) {
			case BoostDirection.PadForward:
				boostDir = transform.forward;
				break;
			case BoostDirection.GravityUp:
				// Uses the car's already-computed up axis so it matches the planet surface.
				boostDir = car.GetComponent<GravityCar>().UpAxis;
				break;
			default: // CarForward
				boostDir = rb.transform.forward;
				break;
		}
		boostDir.Normalize();

		// ── Build the impulse ─────────────────────────────────────────
		// Project existing velocity onto the boost axis.
		float existingComponent = Vector3.Dot(rb.linearVelocity, boostDir);

		// Target speed in boost direction.
		float targetComponent = Mathf.Max(existingComponent, boostSpeed);

		// Blend between "replace" (additive=0) and "add on top" (additive=1).
		// Replace: set the axis component to targetComponent.
		// Add:     just add boostSpeed directly.
		float replaceImpulse = targetComponent - existingComponent;           // closes gap
		float addImpulse     = boostSpeed;                                    // raw add
		float impulse        = Mathf.Lerp(replaceImpulse, addImpulse, additive);

		rb.AddForce(boostDir * impulse, ForceMode.VelocityChange);
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

		Gizmos.color = ready ? new Color(1f, 0.85f, 0.1f, 0.85f)
		                     : new Color(0.4f, 0.4f, 0.4f, 0.5f);
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

		// ── Simulate arc ─────────────────────────────────────────────
		Vector3 pos = origin;
		Vector3 vel = boostDir * boostSpeed;
		float   t   = 0f;

		Vector3 prevPos      = pos;
		bool    hitGround    = false;
		Vector3 landingPoint = Vector3.zero;
		Vector3 landingNormal = Vector3.up;

		for (int i = 0; i < trajectorySteps; i++) {
			float frac = (float)i / trajectorySteps;

			// Gravity at the current position.
			// In edit mode sources are empty — fall back to Physics.gravity so the
			// preview still curves rather than drawing a straight line.
			Vector3 gravity = CustomGravity.GetGravity(pos);
			if (gravity.sqrMagnitude < 0.001f) gravity = Physics.gravity;

			Vector3 nextVel = vel + gravity * trajectoryTimeStep;
			Vector3 nextPos = pos + vel * trajectoryTimeStep
			                      + gravity * (0.5f * trajectoryTimeStep * trajectoryTimeStep);
			t += trajectoryTimeStep;

			// ── Collision check between steps ────────────────────────
			Vector3 step      = nextPos - pos;
			float   stepLen   = step.magnitude;
			if (stepLen > 0.001f &&
			    Physics.Raycast(pos, step / stepLen, out RaycastHit hit,
			                    stepLen, trajectoryMask,
			                    QueryTriggerInteraction.Ignore))
			{
				// Draw the last partial segment to the landing spot
				Color segCol = TrajectorySegmentColor(frac);
				UnityEditor.Handles.color = segCol;
				UnityEditor.Handles.DrawLine(pos, hit.point, 2.5f);

				hitGround     = true;
				landingPoint  = hit.point;
				landingNormal = hit.normal;
				break;
			}

			// ── Draw segment ─────────────────────────────────────────
			Color c = TrajectorySegmentColor(frac);
			UnityEditor.Handles.color = c;
			UnityEditor.Handles.DrawLine(pos, nextPos, 2.5f);

			// Dot at each step — shrinks and fades with distance
			if (i % 6 == 0) {
				float dotSize = Mathf.Lerp(0.10f, 0.03f, frac);
				Gizmos.color  = c;
				Gizmos.DrawSphere(nextPos, dotSize);
			}

			// Time labels every 0.5 s
			float labelInterval = 0.5f;
			float prevT = t - trajectoryTimeStep;
			if (Mathf.Floor(t / labelInterval) > Mathf.Floor(prevT / labelInterval)) {
				UnityEditor.Handles.Label(nextPos,
					$" {t:F1}s",
					new GUIStyle(GUI.skin.label) {
						normal    = { textColor = new Color(1f, 0.95f, 0.4f, 0.85f) },
						fontSize  = 9,
						fontStyle = FontStyle.Bold
					});
			}

			prevPos = pos;
			pos     = nextPos;
			vel     = nextVel;
		}

		// ── Landing marker ────────────────────────────────────────────
		if (hitGround) {
			// Splat ring aligned to the surface normal
			UnityEditor.Handles.color = new Color(1f, 0.3f, 0.1f, 0.85f);
			UnityEditor.Handles.DrawWireDisc(landingPoint + landingNormal * 0.02f,
			                                  landingNormal, 0.45f);
			UnityEditor.Handles.DrawWireDisc(landingPoint + landingNormal * 0.02f,
			                                  landingNormal, 0.25f);
			Gizmos.color = new Color(1f, 0.3f, 0.1f, 0.7f);
			Gizmos.DrawSphere(landingPoint, 0.12f);

			// Total flight time label
			UnityEditor.Handles.Label(
				landingPoint + landingNormal * 0.6f,
				$" Land  {t:F2}s",
				new GUIStyle(GUI.skin.label) {
					normal    = { textColor = new Color(1f, 0.4f, 0.2f, 1f) },
					fontSize  = 10,
					fontStyle = FontStyle.Bold
				});
		}

		// ── Direction mode label ──────────────────────────────────────
		string modeNote = direction == BoostDirection.CarForward
			? "(CarForward — preview uses Pad Forward)"
			: direction.ToString();
		UnityEditor.Handles.Label(origin + boostDir * 0.6f,
			$" {boostSpeed:F0} m/s  [{modeNote}]",
			new GUIStyle(GUI.skin.label) {
				normal    = { textColor = trajectoryColor },
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

	Color TrajectorySegmentColor (float frac) {
		// Bright and opaque near launch, dim and transparent at the far end.
		return new Color(
			trajectoryColor.r,
			trajectoryColor.g,
			trajectoryColor.b,
			trajectoryColor.a * Mathf.Lerp(1f, 0.15f, frac));
	}
#endif
}
