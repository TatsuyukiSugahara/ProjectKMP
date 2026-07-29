namespace ProjectKMP.Gorilla
{
    /// <summary>
    /// ゴリラAIの各ステートが実装する共通インターフェース。
    /// 待機・徘徊・追跡・攻撃・硬直などの挙動をステートクラスとして実装し、
    /// GorillaAI.ChangeState() でステートを切り替える。
    /// </summary>
    public interface IGorillaState
    {
        /// <summary>ステートに遷移した直後に一度だけ呼ばれる</summary>
        void Enter(GorillaAI owner);

        /// <summary>毎フレーム呼ばれる更新処理</summary>
        void Update(GorillaAI owner);

        /// <summary>ステートから抜ける直前に一度だけ呼ばれる</summary>
        void Exit(GorillaAI owner);
    }
}
