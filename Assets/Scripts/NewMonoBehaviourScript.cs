using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenu : MonoBehaviour
{
    public void PlayGame()
    {
        // Loads the gameplay scene (Index 1 in Build Settings)
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex + 1);
    }

    public void QuitGame()
    {
        // Closes the built game app
        Application.Quit();
        
        // Displays a message in Unity's console window to prove it works
        Debug.Log("Game Exited!");
    }
}