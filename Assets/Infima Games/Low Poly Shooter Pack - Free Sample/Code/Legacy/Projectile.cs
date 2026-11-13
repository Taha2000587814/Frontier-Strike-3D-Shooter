using UnityEngine;
using System.Collections;
using FrontierStrike;
using InfimaGames.LowPolyShooterPack;
using Random = UnityEngine.Random;

public class Projectile : MonoBehaviour {

	[Range(5, 100)]
	[Tooltip("After how long time should the bullet prefab be destroyed?")]
	public float destroyAfter;
	[Tooltip("If enabled the bullet destroys on impact")]
	public bool destroyOnImpact = false;
	[Tooltip("Minimum time after impact that the bullet is destroyed")]
	public float minDestroyTime;
	[Tooltip("Maximum time after impact that the bullet is destroyed")]
	public float maxDestroyTime;

	[Header("Damage")]
	[Tooltip("Damage dealt to anything implementing IDamageable. Set by the weapon that fired this projectile.")]
	public float damage = 25.0f;

	[Header("Impact Effect Prefabs")]
	public Transform [] bloodImpactPrefabs;
	public Transform [] metalImpactPrefabs;
	public Transform [] dirtImpactPrefabs;
	public Transform []	concreteImpactPrefabs;

	private bool hasHit;

	private void Start ()
	{
		//Grab the game mode service, we need it to access the player character!
		var gameModeService = ServiceLocator.Current.Get<IGameModeService>();
		//Ignore the main player character's collision. A little hacky, but it should work.
		CharacterBehaviour player = gameModeService.GetPlayerCharacter();
		if (player != null && player.TryGetComponent(out Collider playerCollider))
			Physics.IgnoreCollision(playerCollider, GetComponent<Collider>());

		//Start destroy timer
		StartCoroutine (DestroyAfter ());
	}

	//If the bullet collides with anything
	private void OnCollisionEnter (Collision collision)
	{
		//Ignore collisions with other projectiles.
		if (collision.gameObject.GetComponent<Projectile>() != null)
			return;

		//Only ever resolve one impact per bullet.
		if (hasHit)
			return;
		hasHit = true;

		ContactPoint contact = collision.GetContact(0);
		Quaternion impactRotation = Quaternion.LookRotation(contact.normal);

		//Damage anything that can take damage (enemies, player, etc.).
		var damageable = collision.collider.GetComponentInParent<IDamageable>();
		if (damageable != null)
		{
			damageable.TakeDamage(new DamageInfo(damage, contact.point, transform.forward, collision.collider, gameObject));
			Destroy(gameObject);
			return;
		}

		Transform t = collision.transform;
		if (t.CompareTag("Blood"))
			SpawnImpact(bloodImpactPrefabs, impactRotation);
		else if (t.CompareTag("Metal"))
			SpawnImpact(metalImpactPrefabs, impactRotation);
		else if (t.CompareTag("Dirt"))
			SpawnImpact(dirtImpactPrefabs, impactRotation);
		else if (t.CompareTag("Target"))
			t.GetComponent<TargetScript>().isHit = true;
		else if (t.CompareTag("ExplosiveBarrel"))
			t.GetComponent<ExplosiveBarrelScript>().explode = true;
		else if (t.CompareTag("GasTank"))
			t.GetComponent<GasTankScript>().isHit = true;
		else
			//Concrete is the default impact, so untagged level geometry still shows bullet hits.
			SpawnImpact(concreteImpactPrefabs, impactRotation);

		//If destroy on impact is false, start coroutine with random destroy timer.
		if (destroyOnImpact)
			Destroy(gameObject);
		else
			StartCoroutine(DestroyTimer());
	}

	private void SpawnImpact(Transform[] prefabs, Quaternion rotation)
	{
		if (prefabs == null || prefabs.Length == 0)
			return;
		Transform prefab = prefabs[Random.Range(0, prefabs.Length)];
		if (prefab != null)
			Instantiate(prefab, transform.position, rotation);
	}

	private IEnumerator DestroyTimer ()
	{
		//Wait random time based on min and max values
		yield return new WaitForSeconds
			(Random.Range(minDestroyTime, maxDestroyTime));
		//Destroy bullet object
		Destroy(gameObject);
	}

	private IEnumerator DestroyAfter ()
	{
		//Wait for set amount of time
		yield return new WaitForSeconds (destroyAfter);
		//Destroy bullet object
		Destroy (gameObject);
	}
}
