-- NOVAIS boost — base anônima de resultados (F21). Nenhuma coluna guarda IP, nome, e-mail, serial ou nome do PC.
CREATE TABLE IF NOT EXISTS results (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  cpu TEXT NOT NULL,
  gpu TEXT NOT NULL,
  ram_gb INTEGER NOT NULL,
  ram_mts INTEGER NOT NULL,
  windows_build TEXT NOT NULL,
  month TEXT NOT NULL,            -- "2026-11": só o mês
  tweak TEXT NOT NULL,            -- uma linha por ajuste do grupo testado
  grp TEXT NOT NULL,
  game TEXT NOT NULL,
  verdict TEXT NOT NULL,
  average_fps_change REAL NOT NULL,
  one_percent_low_change REAL NOT NULL,
  ci_low REAL NOT NULL,
  ci_high REAL NOT NULL,
  app_version TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS results_class ON results (cpu, gpu, tweak);
