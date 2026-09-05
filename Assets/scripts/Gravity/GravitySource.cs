using UnityEngine;

public class GravitySource : MonoBehaviour {

	[SerializeField, Tooltip("Higher priority sources override lower priority sources. Local tracks/boxes default to higher priority than global planets.")]
	int priority = 0;

	public virtual int Priority {
		get => priority;
		set => priority = value;
	}

	public virtual float GravityStrength => 9.81f;

	public virtual Vector3 GetGravity (Vector3 position) {
		return Physics.gravity;
	}

	void OnEnable () {
		CustomGravity.Register(this);
	}

	void OnDisable () {
		CustomGravity.Unregister(this);
	}
}