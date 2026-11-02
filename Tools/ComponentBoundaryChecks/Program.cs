using System;
using MainProjeto.Scripts.ParentCoponent;
using MainProjeto.Scripts.Protoboard_Interactor;
using UnityEngine;

internal static class ComponentBoundaryChecks
{
    private static int checks;
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        checks++;
    }
    private static int Main()
    {
        try
        {
            var center = new Vector3(2, -3, 5);
            var size = new Vector3(4, 6, 2);
            Check(ComponentBoundaryState.ContainsLocalPoint(center, center, size), "Centro precisa pertencer a caixa.");
            foreach (var offset in new[] { new Vector3(2,0,0), new Vector3(-2,0,0), new Vector3(0,3,0), new Vector3(0,-3,0), new Vector3(0,0,1), new Vector3(0,0,-1), new Vector3(2,3,1) })
            {
                Check(ComponentBoundaryState.ContainsLocalPoint(center + offset, center, size), "Faces e cantos fazem parte do volume.");
                Check(!ComponentBoundaryState.ContainsLocalPoint(center + offset * 1.01f, center, size), "Ponto alem da face deve estar fora.");
            }
            Check(ComponentBoundaryState.ContainsLocalPoint(center, center, -size), "Escala refletida nao pode inverter o teste.");

            var state = new ComponentBoundaryState();
            Check(!state.ShouldRecover(false, false, true), "Peca que nasce fora nao pode ser teleportada para zero.");
            Check(!state.ShouldRecover(true, false, false), "Limite desativado nao deve armar.");
            Check(!state.ShouldRecover(false, false, true), "A caixa desativada nao pode gerar recuperacao posterior.");
            Check(!state.ShouldRecover(true, false, true), "Entrar deve apenas guardar posicao.");
            Check(state.ShouldRecover(false, false, true), "Cruzar o limite precisa recuperar.");
            Check(!state.ShouldRecover(false, false, true), "Modo cair nao pode zerar gravidade/velocidade a cada frame.");
            Check(!state.ShouldRecover(true, false, true), "Reentrar rearma.");
            Check(state.ShouldRecover(false, false, true), "Outra saida deve ser tratada.");

            state.Reset();
            Check(!state.ShouldRecover(true, true, true), "Autoconexao dentro nao deve recuperar.");
            for (int i = 0; i < 12; i++)
                Check(!state.ShouldRecover(false, true, true), "Peca na protoboard continua imune mesmo fora da caixa.");
            Check(state.ShouldRecover(false, false, true), "Ao retirar da protoboard o limite volta a valer.");
            state.Reset();
            Check(!state.ShouldRecover(false, false, true), "Trocar de caixa descarta a posicao do limite antigo.");
            Check(!state.ShouldRecover(false, true, true), "Autoconexao que nasce fora deve permanecer intacta.");
            Check(!state.ShouldRecover(false, false, true), "Sem posicao valida nao ha retorno arbitrario.");
            Check(!state.ShouldRecover(true, false, true), "Entrar pela primeira vez registra uma posicao valida.");
            Check(!state.ShouldRecover(false, false, false), "Desabilitar limite durante o movimento nao atua.");
            Check(state.ShouldRecover(false, false, true), "Reabilitar apos entrada permite recuperacao.");
            CheckOccupancy();
            checks += QuestAdvanceChecks.Run();
            Console.WriteLine($"PASS: {checks} verificacoes de limite/ocupacao/avanco de quests; fontes reais compiladas com Unity/XRI.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL: " + error.Message); return 1; }
    }

    private static void CheckOccupancy()
    {
        var map = new ProtoboardOccupancyMap<object>();
        var first = new object(); var next = new object();
        var a = new Vector3Int(-2,-4,0); var b = new Vector3Int(-1,-4,0); var c = new Vector3Int(2,-4,0);
        var footprint = new System.Collections.Generic.List<Vector3Int> { a, b };
        Check(map.IsFree(a, first), "Celula vazia precisa estar livre.");
        map.Reserve(first, footprint);
        Check(map.IsFree(a, first), "A propria peca deve conservar a selecao.");
        Check(!map.IsFree(a, next) && !map.IsFree(b, next), "Outro componente nao pode ocupar os pinos reservados.");
        footprint.Clear(); footprint.Add(c); // Simula mover/rotacionar a peca antes da saida.
        map.Release(first);
        Check(map.IsFree(a, next) && map.IsFree(b, next), "Saida deve liberar o snapshot original, sem recalcular pinos.");
        map.Reserve(first, new[] { a }); map.Reserve(first, new[] { c });
        Check(map.IsFree(a, next) && !map.IsFree(c, next), "Nova reserva do mesmo dono precisa liberar a antiga.");
        map.Reserve(next, new[] { c }); map.Release(first);
        Check(!map.IsFree(c, first), "Liberar dono antigo nao pode remover reserva mais recente.");
        map.Release(first); map.Release(null);
        Check(!map.IsFree(c, first), "Saida repetida nao pode alterar outras reservas.");
        Check(map.IsFree(c, first, owner => ReferenceEquals(owner, next)), "Dono destruido nao pode bloquear a proxima quest.");
        map.Reserve(first, new[] { a, b }); map.Clear();
        Check(map.IsFree(a, next) && map.IsFree(b, next), "Desativar protoboard deve limpar todas as reservas.");
    }
}
