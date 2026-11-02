using System;

[Serializable]
public class GazeTelemetryFrame
{
    public float TimeSinceStart; // Mesma base de tempo do carro para sincronizar depois
    
    // Coordenadas Espaciais da Cabeça (HMD)
    public float HeadPositionX;
    public float HeadPositionY;
    public float HeadPositionZ;
    
    // Rotação da Cabeça (Para onde está apontando)
    public float HeadRotationX;
    public float HeadRotationY;
    public float HeadRotationZ;
}