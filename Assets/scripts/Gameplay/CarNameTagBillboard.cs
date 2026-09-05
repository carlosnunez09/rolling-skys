using TMPro;
using UnityEngine;

public class CarNameTagBillboard : MonoBehaviour {

    [SerializeField] Transform targetCar;
    [SerializeField] TMP_Text label;
    [SerializeField] float height = 2.2f;
    [SerializeField] bool hideForLocalPlayer = true;
    [SerializeField] bool scaleByDistance = true;
    [SerializeField] float baseScale = 1f;
    [SerializeField] float minScale = 0.6f;
    [SerializeField] float maxScale = 1.4f;
    [SerializeField] float referenceDistance = 18f;

    Camera _camera;
    MovingCar _movingCar;
    Vector3 _initialLocalScale;

    void Awake () {
        _initialLocalScale = transform.localScale;

        if (label == null)
            label = GetComponentInChildren<TMP_Text>(true);

        if (targetCar == null) {
            MovingCar parentCar = GetComponentInParent<MovingCar>();
            if (parentCar != null)
                targetCar = parentCar.transform;
        }

        if (targetCar != null)
            _movingCar = targetCar.GetComponent<MovingCar>();
    }

    void LateUpdate () {
        if (_camera == null)
            _camera = Camera.main;

        if (_camera == null || targetCar == null) return;

        bool isLocalPlayer = _movingCar != null && _movingCar.HasLocalControl;
        if (hideForLocalPlayer && isLocalPlayer) {
            SetVisible(false);
            return;
        }

        SetVisible(true);

        transform.position = targetCar.position + targetCar.up * height;

        Vector3 toCamera = transform.position - _camera.transform.position;
        if (toCamera.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(toCamera, _camera.transform.up);

        if (scaleByDistance) {
            float distance = toCamera.magnitude;
            float distanceScale = Mathf.Clamp(distance / Mathf.Max(referenceDistance, 0.01f), minScale, maxScale);
            transform.localScale = _initialLocalScale * (baseScale * distanceScale);
        } else {
            transform.localScale = _initialLocalScale * baseScale;
        }
    }

    public void SetText (string displayName) {
        if (label != null)
            label.text = displayName;
    }

    void SetVisible (bool visible) {
        if (label != null && label.gameObject.activeSelf != visible)
            label.gameObject.SetActive(visible);
    }
}
