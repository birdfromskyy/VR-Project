using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;

public class ShiftManager : MonoBehaviour
{
    [Header("Links")]
    [SerializeField] private CustomerManager customerManager;
    [SerializeField] private OrderManager orderManager;
    [SerializeField] private WinnerScreen winnerScreen;
    [SerializeField] private ShiftIntroScreen introScreen;
    [SerializeField] private GameOverScreen gameOverScreen;

    [Header("Optional UI (can be null)")]
    [SerializeField] private TMP_Text shiftText;
    [SerializeField] private TMP_Text statusText;

    [Header("Shift Rules")]
    [SerializeField] private int targetShifts = 3;

    [System.Serializable]
    public struct DaySettings
    {
        public int day;

        [Header("Customers")]
        public int targetCustomers;
        public float spawnInterval;

        [Header("Recipe")]
        public int recipeIngredientsTotal;

        [Header("Mood by time")]
        public float happyTime;
        public float neutralTime;
        public float angryTime;

        [Header("Cooking")]
        public float pattyCookTime;

        [Header("Ingredients spawning")]
        public float ingredientSpawnInterval;
        public int ingredientMaxAlive;
    }

    [SerializeField] private DaySettings[] days = new DaySettings[]
    {
        new DaySettings{ day=1, targetCustomers=3,  recipeIngredientsTotal=3, happyTime=60, neutralTime=60, angryTime=40, pattyCookTime=22, spawnInterval=25, ingredientSpawnInterval=20, ingredientMaxAlive=3 },
        new DaySettings{ day=2, targetCustomers=6,  recipeIngredientsTotal=4, happyTime=43, neutralTime=43, angryTime=25, pattyCookTime=18, spawnInterval=19, ingredientSpawnInterval=14, ingredientMaxAlive=4 },
        new DaySettings{ day=3, targetCustomers=10, recipeIngredientsTotal=6, happyTime=25, neutralTime=25, angryTime=10, pattyCookTime=14, spawnInterval=13, ingredientSpawnInterval=9,  ingredientMaxAlive=5 },
    };

    // Сохраняем день между перезагрузками сцены
    public static int StartDay = 1;

    [Header("Fire fail delay")]
    [SerializeField] private float fireFailDelay = 2f;

    private int currentShift = 1;

    private int currentTargetClients = 0;
    private int clientsServedThisShift = 0;

    private bool shiftRunning = false;
    private bool isTransitioning = false;

    private int smokeBeepsThisShift = 0;

    private readonly HashSet<Customer> countedCustomers = new HashSet<Customer>();

    private void Awake()
    {
        if (customerManager == null) customerManager = FindObjectOfType<CustomerManager>();
        if (orderManager == null) orderManager = FindObjectOfType<OrderManager>();
    }

    public void FailAfterCustomerDeath(Customer victim, string reason)
    {
        if (!gameObject.activeInHierarchy) return;
        if (isTransitioning) return; // Уже в переходе, не запускаем повторно
        StartCoroutine(FailAfterCustomerDeathRoutine(victim, reason));
    }

    private IEnumerator FailAfterCustomerDeathRoutine(Customer victim, string reason)
    {
        shiftRunning = false;
        isTransitioning = true;

        customerManager?.SetSpawningEnabled(false);

        // Ждём, если проигрываем из-за смерти клиента
        if (victim != null)
            yield return victim.WaitForDeathAnimation();

        if (gameOverScreen != null)
            yield return gameOverScreen.Play(reason);
        else
            yield return new WaitForSeconds(2f);

        StartDay = currentShift;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void OnEnable()
    {
        PattyCookable.OnSmokeAlarmBeepGlobal += OnSmokeBeep;

        if (orderManager != null)
            orderManager.OnCustomerReactionFinished += OnCustomerReactionFinished;

        if (customerManager != null)
            customerManager.OnCustomerGaveUpWaiting += OnCustomerGaveUpWaiting;
    }

    private void OnDisable()
    {
        PattyCookable.OnSmokeAlarmBeepGlobal -= OnSmokeBeep;

        if (orderManager != null)
            orderManager.OnCustomerReactionFinished -= OnCustomerReactionFinished;

        if (customerManager != null)
            customerManager.OnCustomerGaveUpWaiting -= OnCustomerGaveUpWaiting;
    }

    private void OnCustomerGaveUpWaiting(Customer customer)
    {
        if (!shiftRunning || isTransitioning) return;

        // Переходим в состояние проигрыша
        isTransitioning = true;

        StartCoroutine(GameOverAfterCustomerLeavesRoutine(customer, "Клиент ждал слишком долго"));
    }

    private IEnumerator GameOverAfterCustomerLeavesRoutine(Customer leavingCustomer, string reason)
    {
        shiftRunning = false;

        // Останавливаем спавн клиентов
        customerManager?.SetSpawningEnabled(false);

        // Ждём, пока объект клиента исчезнет (Destroy -> ссылка станет null)
        float timeout = 12f;
        float t = 0f;

        while (leavingCustomer != null && t < timeout)
        {
            t += Time.deltaTime;
            yield return null;
        }

        // Показываем Game Over
        if (gameOverScreen != null)
            yield return gameOverScreen.Play(reason);
        else
            yield return new WaitForSeconds(2f);

        StartDay = currentShift;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void Start()
    {
        // Проверяем, что StartDay в допустимых пределах
        if (StartDay < 1) StartDay = 1;
        if (StartDay > targetShifts) StartDay = 1;

        currentShift = StartDay;
        StartCoroutine(StartDayIntroAndStartShift());
    }

    private void Update()
    {
        if (!shiftRunning || isTransitioning) return;

        if (shiftText != null)
            shiftText.text = $"Цель {currentShift}/{targetShifts} Смена: {clientsServedThisShift}/{currentTargetClients}";
    }

    private DaySettings GetDay(int day)
    {
        for (int i = 0; i < days.Length; i++)
            if (days[i].day == day) return days[i];

        return days.Length > 0 ? days[days.Length - 1] : default;
    }

    private IEnumerator StartDayIntroAndStartShift()
    {
        DaySettings s = GetDay(currentShift);

        // На старте не спавним клиентов и не начинаем смену
        customerManager?.SetSpawningEnabled(false);

        // Интро-экран смены
        if (introScreen != null)
            yield return introScreen.Play(currentShift, s.targetCustomers);

        // Применяем настройки дня
        if (orderManager != null)
            orderManager.ApplyDaySettings(s.happyTime, s.neutralTime, s.angryTime, s.recipeIngredientsTotal);

        if (customerManager != null)
            customerManager.ConfigureShiftSpawning(s.targetCustomers, s.spawnInterval);

        PattyCookable.CookTimeSeconds = Mathf.Max(0.1f, s.pattyCookTime);

        ApplyIngredientSpawners(s.ingredientSpawnInterval, s.ingredientMaxAlive);

        // Запуск смены
        StartShiftInternal(s.targetCustomers);

        if (statusText != null)
            statusText.text = "Смена началась";
    }

    private void StartShiftInternal(int targetClients)
    {
        countedCustomers.Clear();

        currentTargetClients = Mathf.Max(1, targetClients);
        clientsServedThisShift = 0;

        smokeBeepsThisShift = 0;

        shiftRunning = true;
        isTransitioning = false;
    }

    private void ApplyIngredientSpawners(float interval, int maxAlive)
    {
        interval = Mathf.Max(0.1f, interval);
        maxAlive = Mathf.Max(1, maxAlive);

        var spawners = FindObjectsOfType<IngredientSpawner>(true);
        foreach (var sp in spawners)
            sp.ApplyDaySettings(interval, maxAlive);
    }

    private void OnSmokeBeep()
    {
        if (!shiftRunning || isTransitioning) return;

        isTransitioning = true;
        shiftRunning = false;

        customerManager?.SetSpawningEnabled(false);

        StartCoroutine(FireGameOverRoutine());
    }

    private IEnumerator FireGameOverRoutine()
    {
        if (statusText != null)
            statusText.text = "Пожар!";

        // Ждём 5 секунд, чтобы было время среагировать
        yield return new WaitForSeconds(5f);

        if (gameOverScreen != null)
            yield return gameOverScreen.Play("Пожар!");

        StartDay = currentShift;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private IEnumerator FailAfterEvacuationRoutine(string reason)
    {
        shiftRunning = false;
        isTransitioning = true;

        // Останавливаем спавн клиентов
        customerManager?.SetSpawningEnabled(false);

        // Ждём перед показом Game Over
        yield return new WaitForSeconds(fireFailDelay);

        // Показываем Game Over
        if (gameOverScreen != null)
            yield return gameOverScreen.Play(reason);
        else
            yield return new WaitForSeconds(2f);

        StartDay = currentShift;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void OnCustomerReactionFinished(Customer customer, CustomerMood mood)
    {
        if (!shiftRunning || isTransitioning) return;

        // Game Over только при злой реакции
        if (mood == CustomerMood.Angry)
        {
            // "Вечно злой" не завершает игру
            if (customer == null || !customer.alwaysAngry)
            {
                FailRun("Клиент недоволен");
                return;
            }

            // alwaysAngry считаем обслуженным и продолжаем
            CountServedOnce(customer);
            TryFinishShift();
            return;
        }

        // Happy/Neutral: просто засчитываем
        if (customer != null)
            CountServedOnce(customer);

        TryFinishShift();
    }

    private void CountServedOnce(Customer customer)
    {
        if (!countedCustomers.Add(customer)) return;
        clientsServedThisShift++;
    }

    private void TryFinishShift()
    {
        if (clientsServedThisShift >= currentTargetClients)
        {
            isTransitioning = true;
            StartCoroutine(EndShiftSuccessRoutine());
        }
    }

    private IEnumerator EndShiftSuccessRoutine()
    {
        shiftRunning = false;
        isTransitioning = true;

        customerManager?.SetSpawningEnabled(false);

        if (winnerScreen != null)
            yield return winnerScreen.PlayShiftCleared(currentShift, targetShifts);
        else
            yield return new WaitForSeconds(1.5f);

        // Последняя смена
        if (currentShift >= targetShifts)
        {
            if (winnerScreen != null)
                yield return winnerScreen.PlayFinalVictory();

            StartDay = 1;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            yield break;
        }

        // Следующая смена
        StartDay = currentShift + 1;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void FailRun(string reason)
    {
        if (!gameObject.activeInHierarchy) return;
        StartCoroutine(FailRoutine(reason));
    }

    private IEnumerator FailRoutine(string reason)
    {
        shiftRunning = false;
        isTransitioning = true;

        customerManager?.SetSpawningEnabled(false);

        if (gameOverScreen != null)
            yield return gameOverScreen.Play(reason);
        else
            yield return new WaitForSeconds(2f);

        StartDay = currentShift;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
