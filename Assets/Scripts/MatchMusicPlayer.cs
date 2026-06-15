using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class MatchMusicPlayer : MonoBehaviour
{
    [Header("Music Settings")]
    [SerializeField] private AudioClip menuMusic;
    [SerializeField] private AudioClip battleMusic;
    [SerializeField][Range(0f, 1f)] private float musicVolume = 0.5f;

    private AudioSource audioSource;

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.loop = true;
        audioSource.volume = musicVolume;
    }

    private void Start()
    {
        // Start playing menu music as a baseline fallback immediately
        PlayTrack(menuMusic);
    }

    private void Update()
    {
        // Wait until the UI Manager exists in the scene
        if (GameUIManager.Instance == null) return;

        // Continuously evaluate the match state. 
        // PlayTrack automatically ignores the call if the correct track is already playing!
        if (GameUIManager.Instance.matchStarted.Value)
        {
            PlayTrack(battleMusic); // Hot drop: match is already running!
        }
        else
        {
            PlayTrack(menuMusic); // Normal lobby spawn
        }
    }

    private void PlayTrack(AudioClip newClip)
    {
        if (newClip == null) return;

        // 👈 THE MAGIC GUARD: If the correct song is already playing, do absolutely nothing!
        // This makes it 100% safe to call this method every frame in Update().
        if (audioSource.clip == newClip && audioSource.isPlaying) return;

        audioSource.Stop();
        audioSource.clip = newClip;
        audioSource.Play();
    }
}