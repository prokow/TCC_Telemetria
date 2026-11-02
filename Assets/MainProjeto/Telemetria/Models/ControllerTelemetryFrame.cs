using System;

[Serializable]
public class ControllerTelemetryFrame
{
    // Tempo decorrido desde o início da sessão de gravação.
    // Serve para sincronizar esta amostra com as demais telemetrias.
    public float TimeSinceStart;

    // Indica se o controlador/mão esquerda estava sendo rastreado
    // no momento da coleta desta amostra.
    // true = rastreado | false = sem rastreio
    public bool IsLeftTracked;

    // Posição da mão/controlador esquerdo no espaço 3D (eixo X).
    public float LeftPosX;

    // Posição da mão/controlador esquerdo no espaço 3D (eixo Y).
    public float LeftPosY;

    // Posição da mão/controlador esquerdo no espaço 3D (eixo Z).
    public float LeftPosZ;

    // Rotação da mão/controlador esquerdo em graus no eixo X.
    public float LeftRotX;

    // Rotação da mão/controlador esquerdo em graus no eixo Y.
    public float LeftRotY;

    // Rotação da mão/controlador esquerdo em graus no eixo Z.
    public float LeftRotZ;

    // Valor horizontal do analógico esquerdo.
    // Normalmente varia entre -1 e 1.
    // Pode representar deslocamento lateral ou rotação, dependendo da ação configurada.
    public float LeftStickX;

    // Valor vertical do analógico esquerdo.
    // Normalmente varia entre -1 e 1.
    // Pode representar avanço/recuo ou outra ação analógica.
    public float LeftStickY;

    // Indica se o controlador/mão direita estava sendo rastreado
    // no momento da coleta desta amostra.
    // true = rastreado | false = sem rastreio
    public bool IsRightTracked;

    // Posição da mão/controlador direito no espaço 3D (eixo X).
    public float RightPosX;

    // Posição da mão/controlador direito no espaço 3D (eixo Y).
    public float RightPosY;

    // Posição da mão/controlador direito no espaço 3D (eixo Z).
    public float RightPosZ;

    // Rotação da mão/controlador direito em graus no eixo X.
    public float RightRotX;

    // Rotação da mão/controlador direito em graus no eixo Y.
    public float RightRotY;

    // Rotação da mão/controlador direito em graus no eixo Z.
    public float RightRotZ;

    // Valor horizontal do analógico direito.
    // Normalmente varia entre -1 e 1.
    public float RightStickX;

    // Valor vertical do analógico direito.
    // Normalmente varia entre -1 e 1.
    public float RightStickY;
}