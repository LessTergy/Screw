# P4.1: координатор хода и отмена попытки

[Индекс и приёмка](../development-plan/README.md) · [План P4](../development-plan/p4-playable-loop.md)

TurnCoordinator управляет порядком операций одного хода. LevelAttempt остаётся владельцем резервирования, маршрутизации, креплений, сортировки и результата. Координатор получает готовый LevelAttempt и ITurnOperations; использует уже установленный UniTask. Ссылки на его сборку добавлены в Screw.Runtime и Screw.EditMode.Tests.

## Последовательность

PlayAsync(boltId) сразу вызывает ReserveBolt. Отклонённый ввод возвращает исходный BoltReservationResult и не запускает визуальные операции; очередь нажатий не хранится. Для принятого хода:

1. Await DetachAsync → ConfirmDetachment: выход из крепления уменьшает счётчик владельца; ReleasedPartId становится доступен операциям следующей фазы.
2. Await ArriveAsync → ConfirmArrival: прибытие запускает логическую сортировку и формирует SortingSteps.
3. Await FinishAsync → CompleteTurn: блокировка сохраняется до завершения визуального исполнения сортировки и ожиданий удаления освобождённой детали.

ITurnOperations описывает границы реальных операций P4.2–P4.4. DetachAsync/ArriveAsync/FinishAsync получают тот же AttemptTurn и CancellationToken попытки. Реализация операций должна менять представление, передавать токен во вложенные ожидания и останавливать свои твины/физические callbacks при отмене. Логические Confirm-методы вызывает координатор. Код рассчитан на вызовы из игрового потока Unity.

## Отмена и ошибки

Restart сначала создаёт свежую попытку через LevelAttempt.Restart и новый CancellationTokenSource, затем отменяет и освобождает прежний. Dispose отменяет ожидания и запрещает новые команды; повторный Dispose допустим.

Токен передаётся каждой операции. AttachExternalCancellation дополнительно немедленно прекращает ожидание координатора, даже если операция игнорирует отмену. После ожидания проверяются токен и принадлежность текущей попытке. Позднее завершение старого ожидания не вызывает Confirm-методы новой попытки. Остановка изменений Transform и уничтожение объектов принадлежат конкретным визуальным операциям и сбросу представления P4.2–P4.5.

Отмена возвращается вызывающему коду как OperationCanceledException. Прочие ошибки операций также передаются вызывающему коду; текущий ход остаётся заблокированным до Restart, чтобы не разрешить следующий ход поверх частично выполненной анимации. При подключении выбора P4.2 вызывающий код должен наблюдать UniTask и явно обрабатывать ошибки намеренного запуска без ожидания.

## Проверка и передача P4.2

15 случаев TurnCoordinatorTests используют управляемые UniTaskCompletionSource только в тестах. Проверяются переходы по одному завершению, отклонение повторного ввода во всех трёх фазах без очереди, немедленная отмена и поздние callbacks после restart/dispose, несколько сбросов, ошибочный boltId и исключения каждой операции. Перед чтением результата Task тест подтверждает его завершение; блокирующих ожиданий и задержек по времени нет.

04.11.2026 через Unity MCP выполнена вся Screw.EditMode.Tests: **277 Passed, 0 Failed, 0 Skipped**, включая эти 15 новых случаев и регрессию P1–P3. После компиляции Console без ошибок.

В Unity дополнительных ссылок Inspector для P4.1 назначать не требуется. Для повторения проверки выполнить Test Runner → EditMode → Screw.Tests.Gameplay.TurnCoordinatorTests, ожидаются 15 Passed. В P4.2 GameplayLifetimeScope получил явную ссылку на готовый LevelGameplayDefinition и зарегистрировал координатор с BoltTurnOperations; GameplaySession передаёт BoltSelection.Selected в ход. [Настоящие операции и проверка P4.2](bolt-turns.md).

Контракт UniTask проверен через Context7 по [официальной документации отмены](https://github.com/Cysharp/UniTask#cancellation-and-exception-handling), [AttachExternalCancellation](https://github.com/Cysharp/UniTask/blob/master/_autodocs/api-reference/unitask-extensions.md) и [UniTaskCompletionSource](https://github.com/Cysharp/UniTask/blob/master/_autodocs/api-reference/unitask-completion-source.md).
