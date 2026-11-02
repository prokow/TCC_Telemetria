using System;
using System.Collections;
using MainProjeto.Robot.Scripts;
using UnityEngine;

public class ParticleController : MonoBehaviour
{
    [Header("Configuração do Efeito")]
    [SerializeField] private ParticleSystem conffeti_1;

    [SerializeField] private ParticleSystem conffeti_2;
    [Tooltip("Tempo em segundos que a partícula ficará ativa antes de desligar.")]
    [SerializeField] private float tempoDeAtivacao = 2f;

    [Tooltip("Desativar o GameObject por completo após o tempo acabar?")]
    [SerializeField] private bool desativarGameObjectAoFinal = false;

    [SerializeField] private AudioClip _soundFinish;
    private AudioSource _audioSource;
    
    private void Awake()
    {
        conffeti_1.gameObject.SetActive(false);
        conffeti_2.gameObject.SetActive(false);
        _audioSource = GetComponent<AudioSource>();
    }


    public void AtivarEfeitoTemporario()
    {
        if (conffeti_1 != null && conffeti_2 != null)
        {
            StartCoroutine(RotinaParticulas());
        }
        else
        {
            Debug.LogWarning("Nenhum ParticleSystem referenciado no TemporaryParticleController.");
        }
    }

    private IEnumerator RotinaParticulas()
    {
        yield return new WaitForSeconds(3.0f);
        
        // Garante que o objeto está ativo e dá o play
        conffeti_1.gameObject.SetActive(true);
        conffeti_2.gameObject.SetActive(true);
        conffeti_1.Play();
        conffeti_2.Play();
        
        _audioSource.PlayOneShot(_soundFinish, 2.0f);
        
        // Aguarda a duração especificada
        yield return new WaitForSeconds(tempoDeAtivacao);
            
        // Para a emissão de partículas suavemente
        conffeti_1.Stop();
        conffeti_2.Stop();

        // Opcional: desliga o GameObject inteiro caso prefira
        if (desativarGameObjectAoFinal)
        {
            // Espera as partículas que já nasceram sumirem da tela antes de desligar
            yield return new WaitForSeconds(conffeti_1.main.startLifetime.constantMax);
            yield return new WaitForSeconds(conffeti_2.main.startLifetime.constantMax);
            conffeti_1.gameObject.SetActive(false);
            conffeti_2.gameObject.SetActive(false);
        }
    }
}
