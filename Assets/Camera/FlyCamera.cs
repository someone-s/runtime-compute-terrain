// Credit https://gist.github.com/FreyaHolmer/650ecd551562352120445513efa1d952
// For testing only
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent( typeof(Camera) )]
public class FlyCamera : MonoBehaviour {
	public float acceleration = 50; // how fast you accelerate
	public float accSprintMultiplier = 4; // how much faster you go when "sprinting"
	public float lookSensitivity = 1; // mouse look sensitivity
	public float dampingCoefficient = 5; // how quickly you break to a halt after you stop your input

	Vector3 velocity; // current velocity

	void Update() {
		UpdateInput();

		// Physics
		velocity = Vector3.Lerp( velocity, Vector3.zero, dampingCoefficient * Time.deltaTime );
		transform.position += velocity * Time.deltaTime;
	}

	private Vector3 skew = Vector3.zero;
	private Vector2 panValue = Vector2.zero;
	private bool panActive = false;

	void UpdateInput() {
		// Position
		velocity += skew * acceleration * Time.deltaTime;

		// Rotation
		if (panActive)
		{
			Vector2 mouseDelta = lookSensitivity * panValue;
			Quaternion rotation = transform.rotation;
			Quaternion horiz = Quaternion.AngleAxis( mouseDelta.x, Vector3.up );
			Quaternion vert = Quaternion.AngleAxis( mouseDelta.y, Vector3.right );
			transform.rotation = horiz * rotation * vert;
		}
	}

	public void OnMove(InputAction.CallbackContext context)
	{
		Vector3 moveInput = Vector3.zero;

		Vector2 input = context.ReadValue<Vector2>();
		moveInput += Vector3.forward * input.y;
		moveInput += Vector3.right * input.x;

		skew = transform.TransformVector(moveInput.normalized);
	}

	public void OnLook(InputAction.CallbackContext context)
	{
		Vector2 input = context.ReadValue<Vector2>();
		input.y = -input.y;
		panValue = input;
	}

	public void OnCrouch(InputAction.CallbackContext context)
	{
		bool oldValue = panActive;
		panActive = context.ReadValueAsButton();
		if (oldValue != panActive)
		{
			if (panActive)
				Cursor.lockState = CursorLockMode.Locked;
			else
				Cursor.lockState = CursorLockMode.None;
		}
	}
}