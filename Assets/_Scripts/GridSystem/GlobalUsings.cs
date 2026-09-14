// Match3 originally kept GridSystem2D in the Match3Game namespace, so most files never
// declared a `using` for it. It now lives in the JadedBelles.Util package. This global
// using preserves the old lack-of-friction: any file in Assembly-CSharp can keep referring
// to GridSystem2D<T> and GridCell<T, U> unqualified.
global using JadedBelles.Util.GridSystem;
