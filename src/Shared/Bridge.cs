using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace SheetViewsMap
{
    /// <summary>
    /// Мост между панелью и Revit API.
    ///
    /// Панель — обычный WPF-элемент, и к Revit API из неё обращаться нельзя:
    /// Revit выдаст ошибку «API context». Поэтому панель только просит здесь
    /// «дай данные» / «открой листы», а саму работу выполняют обработчики
    /// ExternalEvent — Revit сам вызывает их в правильном контексте.
    ///
    /// Блокировки добавлены «на всякий случай»: фактически панель и обработчики
    /// живут в одном потоке — в главном потоке Revit.
    /// </summary>
    internal static class Bridge
    {
        private static readonly object Sync = new object();

        private static ExternalEvent _refreshEvent;
        private static ExternalEvent _navigateEvent;

        private static SheetMapData _lastData;
        private static bool _refreshDone;

        /// <summary>
        /// Листы, которые ещё нужно открыть. Revit умеет показывать только один
        /// вид за раз, поэтому листы открываются по очереди: обработчик берёт
        /// первый из очереди, а панель по таймеру запускает следующий.
        /// </summary>
        private static readonly Queue<ElementId> SheetQueue = new Queue<ElementId>();

        private static int _queueTotal;

        private static string _lastNavigateError;
        private static bool _navigateDone;

        /// <summary>Готов ли плагин к работе (события созданы в OnStartup).</summary>
        internal static bool IsReady
        {
            get { return _refreshEvent != null && _navigateEvent != null; }
        }

        internal static void Initialize()
        {
            if (IsReady) return;

            _refreshEvent = ExternalEvent.Create(new RefreshHandler());
            _navigateEvent = ExternalEvent.Create(new NavigateHandler());
        }

        // ------------------------------------------------------------------ чтение модели

        /// <summary>Просит перечитать листы. Возвращает false, если предыдущий запрос ещё в работе.</summary>
        internal static bool RequestRefresh()
        {
            ExternalEvent ev = _refreshEvent;
            if (ev == null || ev.IsPending) return false;

            lock (Sync)
            {
                _refreshDone = false;
                _lastData = null;
            }

            ev.Raise();
            return true;
        }

        /// <summary>Забирает готовый результат чтения. true — данные пришли, можно рисовать.</summary>
        internal static bool TryTakeData(out SheetMapData data)
        {
            lock (Sync)
            {
                if (!_refreshDone)
                {
                    data = null;
                    return false;
                }

                _refreshDone = false;
                data = _lastData;
                return true;
            }
        }

        internal static void PublishData(SheetMapData data)
        {
            lock (Sync)
            {
                _lastData = data;
                _refreshDone = true;
            }
        }

        // ------------------------------------------------------------------ открытие листов

        /// <summary>
        /// Ставит листы в очередь и запускает открытие первого.
        /// </summary>
        internal static bool RequestOpenSheets(IEnumerable<ElementId> sheetIds)
        {
            // Сначала наполняем очередь, потом запускаем: так состояние
            // всегда соответствует запросу, даже если событие ещё недоступно.
            lock (Sync)
            {
                SheetQueue.Clear();

                foreach (ElementId id in sheetIds)
                {
                    if (id != null && id != ElementId.InvalidElementId)
                        SheetQueue.Enqueue(id);
                }

                _queueTotal = SheetQueue.Count;
                _navigateDone = false;
                _lastNavigateError = null;
            }

            if (_queueTotal == 0)
                return false;

            // Если событие ещё не создано, очередь НЕ очищаем: таймер панели
            // продолжает пытаться открыть листы, когда Revit освободится.
            return RequestOpenNext();
        }

        /// <summary>Запускает обработчик для следующего листа из очереди.</summary>
        internal static bool RequestOpenNext()
        {
            ExternalEvent ev = _navigateEvent;
            if (ev == null || ev.IsPending) return false;

            if (!HasPendingSheets)
                return false;

            ev.Raise();
            return true;
        }

        internal static bool HasPendingSheets
        {
            get
            {
                lock (Sync) { return SheetQueue.Count > 0; }
            }
        }

        /// <summary>Сколько листов ещё в очереди и сколько было всего.</summary>
        internal static void GetQueueState(out int remaining, out int total)
        {
            lock (Sync)
            {
                remaining = SheetQueue.Count;
                total = _queueTotal;
            }
        }

        internal static ElementId TakeNextSheetId()
        {
            lock (Sync)
            {
                if (SheetQueue.Count == 0)
                    return null;

                return SheetQueue.Dequeue();
            }
        }

        internal static void ClearPendingSheets()
        {
            lock (Sync) { SheetQueue.Clear(); }
        }

        internal static void PublishNavigateResult(string error)
        {
            lock (Sync)
            {
                _lastNavigateError = error;
                _navigateDone = true;
            }
        }

        /// <summary>Забирает результат открытия. error == null — открылось без ошибок.</summary>
        internal static bool TryTakeNavigateError(out string error)
        {
            lock (Sync)
            {
                if (!_navigateDone)
                {
                    error = null;
                    return false;
                }

                _navigateDone = false;
                error = _lastNavigateError;
                return true;
            }
        }
    }
}