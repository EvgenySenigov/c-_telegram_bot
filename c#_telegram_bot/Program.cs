using System;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using static System.Formats.Asn1.AsnWriter;
using Newtonsoft;
using Newtonsoft.Json;
using Telegram.Bot.Types.ReplyMarkups;
using System.Data;
using System.Linq;

namespace TelegramBot
{
    class Program
    {
        //создаём бота, передаём в него системный токен, который получен от Telegram API
        private static ITelegramBotClient bot = new TelegramBotClient("6165197946:AAF9pjz0XADxl73qRqNGJQEK_xi3bZyuczY");
        //объявление объекта quiz
        private static Quiz? quiz;
        //объявление объекта chatId который хранит идентификатор чата конкретного пользователя с ботом
        private static long chatId;
        private static long fromId;
        private static string userName;
        //объявление словарей, которые хранят пары ключ-объект
        //здесь ключ - это chatId, т.е. идентификатор чата, а объект - это объект типа QuestionState, который хранит состояние текущего вопроса у пользователя
        private static Dictionary<long, QuestionState>? UserStates;
        //а здесь ключ - это fromId, т.е. идентификатор пользователя, а объект - это объект типа UserScore, в котором хранится имя пользователя и его результат очков
        private static Dictionary<long, UserScore>? UserScores;
        //имена файлов, в которые мы будем сохранять состояния вопросов в разных чатах и результаты пользователей, то есть словари UserStates и UserScores
        private static string StateFilename = "state.txt";
        private static string ScoreFilename = "score.txt";
        //константы, которые хранят текст, который посылают соответствующие кнопки
        private const string StartButtonText = "/start";
        private const string QuizButtonText = "/quiz";
        private const string TopButtonText = "/top";
        private const string ResultButtonText = "/result";
        private const string ResetButtonText = "/reset";
        private const string FeedbackButtonText = "/feedback";
        //метод-обработчик апдейтов от сервера
        public static async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken cancellationToken)
        {
            //форматируем апдейт в джсон и выводим его в консоль
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(update));
            //если тип апдейта это какое то сообщение
            if (update.Type == Telegram.Bot.Types.Enums.UpdateType.Message)
            {
                //если сообщение это текст (обрабатываем только текст)
                if (update.Message.Type == Telegram.Bot.Types.Enums.MessageType.Text)
                {
                    var message = update.Message;
                    chatId = message.Chat.Id;
                    fromId = message.From.Id;
                    userName = message.From.Username;
                    //обработка сообщений
                    switch (message.Text)
                    {
                        //начальное сообщение и список команд
                        case ("/start"):
                            await bot.SendTextMessageAsync(chatId, "Добро пожаловать!\nДля начала викторины, нажмите кнопку или наберите /quiz.\n" +
                                "Для того, чтобы вернуться в начало, нажмите кнопку или наберите /start.\n" +
                                "Для того, чтобы увидеть топ игроков, нажмите кнопку или наберите /top.\n" +
                                "Для того, чтобы увидеть свой результат, нажмите кнопку или наберите /result.\n" +
                                "Для того, чтобы начать игру заново, нажмите кнопку или наберите /reset.\n" +
                                "Для того, чтобы отправить свои пожелания, нажмите кнопку или наберите /feedback.", replyMarkup: GetDefaultReplyMarkup());
                            break;
                        //команда начала нового раунда
                        case ("/quiz"):
                            await NewRound(chatId);
                            break;
                        //команда для вывода топа игроков
                        case ("/top"):
                            await ShowTop(chatId);
                            break;
                        //команда для вывода личного результата
                        case ("/result"):
                            await ShowUserResult(chatId, fromId, userName);
                            break;
                        //команда для сброса результата
                        case ("/reset"):
                            await ResetResult(chatId, fromId);
                            break;
                        //команда для обратной связи
                        case ("/feedback"):
                            //встроенная кнопка
                            InlineKeyboardButton urlButton = new InlineKeyboardButton("Связаться с автором");
                            //ссылка на аккаунт автора
                            urlButton.Url = "https://t.me/dedinside192";
                            InlineKeyboardMarkup Keyboard = new InlineKeyboardMarkup(urlButton);
                            await bot.SendTextMessageAsync(chatId, "Для обратной связи вы можете отправить свои пожелания автору.", replyMarkup: Keyboard);
                            break;
                        //обработка ответов на вопрос викторины
                        default:
                            //если текущего состояния пользователя нет в списке то создаём его и инициализируем
                            if (!UserStates.TryGetValue(chatId, out var state))
                            {
                                state = new QuestionState();
                                UserStates[chatId] = state;
                            }
                            //если в текущем состоянии пользователя нет вопроса то инициализируем
                            if (state.CurrentItem == null)
                            {
                                state.CurrentItem = quiz.NextQuestion();
                            }
                            var question = state.CurrentItem;
                            //записываем состояние в файл
                            var stateJson = JsonConvert.SerializeObject(UserStates);
                            System.IO.File.WriteAllText(StateFilename, stateJson);
                            // переменная tryAnswer хранит ответ пользователя на вопрос, переводим её в lowercase и заменяем ё на е тк в списке вопросов нет буквы ё
                            var tryAnswer = message.Text.ToLower().Replace('ё', 'е');
                            //если ответ пользователя совпал с ответом на вопрос
                            if (tryAnswer == question.Answer)
                            {
                                if (UserScores.ContainsKey(fromId))
                                {
                                    //добавляем очки в случае правильного ответа
                                    UserScores[fromId].Score++;
                                    //записываем результаты пользователя в файл
                                    var scoreJson = JsonConvert.SerializeObject(UserScores);
                                    System.IO.File.WriteAllText(ScoreFilename, scoreJson);
                                }
                                //если результата пользователя нет в списке 
                                else
                                {
                                    //создаём результат
                                    UserScores[fromId] = new UserScore();
                                    //инициализируем
                                    UserScores[fromId].Score = 1;
                                    UserScores[fromId].Username = userName;
                                    //записываем в файл
                                    var scoreJson = JsonConvert.SerializeObject(UserScores);
                                    System.IO.File.WriteAllText(ScoreFilename, scoreJson);
                                }
                                await bot.SendTextMessageAsync(chatId, $"Правильно!\nВаш результат: {UserScores[fromId].Score}.", replyMarkup: GetDefaultReplyMarkup());
                                NewRound(chatId);
                            }
                            //если ответ не совпал
                            else
                            {
                                //открываем на 1 букву больше в подсказке
                                state.Opened++;
                                //снова записываем state в файл тк state.opened изменилось
                                stateJson = JsonConvert.SerializeObject(UserStates);
                                System.IO.File.WriteAllText(StateFilename, stateJson);
                                //если открыты все буквы в подсказке то выдаём ответ на старый вопрос и задаём новый 
                                if (state.isEnd)
                                {
                                    await bot.SendTextMessageAsync(chatId, $"Никто не отгадал! Это было - {question.Answer}.", replyMarkup: GetDefaultReplyMarkup());
                                    NewRound(chatId);
                                }
                                //если нет то снова показываем вопрос с подсказкой
                                else
                                {
                                    await bot.SendTextMessageAsync(chatId, $"Неправильно!\n{state.DisplayQuestion}", replyMarkup: GetDefaultReplyMarkup());
                                }
                            }
                            break;
                    }
                }
                //если сообщение не текстовое
                else
                {
                    await bot.SendTextMessageAsync(chatId, "Простите, не понял вас.\nЧтобы вернуться в начало, наберите или нажмите '/start'.", replyMarkup: GetDefaultReplyMarkup());
                }
            }
        }
        //метод, который показывает результаты
        public static async Task ShowTop(long chatId)
        {
            //если есть результаты в словаре, выводим содержимое
            if (UserScores.Count > 0)
            {
                //сортируем с помощью LINQ по убыванию
                var sortedDict = from entry in UserScores orderby entry.Value.Score descending select entry;
                await bot.SendTextMessageAsync(chatId, "Топ игроков:");
                //выводим
                foreach (var score in sortedDict)
                {
                    await bot.SendTextMessageAsync(chatId, $"{score.Value.Username} : {score.Value.Score}");
                }
            }
            //если словарь пока что пустой, сообщаем об этом
            else
            {
                await bot.SendTextMessageAsync(chatId, "Пока что активных пользователей нет!");
            }
        }
        //метод, который показывает результат конкретного пользователя
        public static async Task ShowUserResult(long chatId, long fromId, string userName)
        {
            //если результата нет в списке
            if (!UserScores.ContainsKey(fromId))
            {
                //создаём результат
                UserScores[fromId] = new UserScore();
                //инициализируем
                UserScores[fromId].Score = 0;
                UserScores[fromId].Username = userName;
                //записываем в файл
                var scoreJson = JsonConvert.SerializeObject(UserScores);
                System.IO.File.WriteAllText(ScoreFilename, scoreJson);
            }
            await bot.SendTextMessageAsync(chatId, $"Ваш результат: {UserScores[fromId].Score}.");
        }
        //метод, который обнуляет результат
        public static async Task ResetResult(long chatId, long fromId)
        {
            if ((!UserScores.ContainsKey(fromId)) || (UserScores[fromId].Score == 0))
            {
                await bot.SendTextMessageAsync(chatId, "Ваш результат уже равен 0.");
            }
            else
            {
                //обнуляем результат
                UserScores[fromId].Score = 0;
                //сохраняем обновлённый результат в файл
                var scoreJson = JsonConvert.SerializeObject(UserScores);
                System.IO.File.WriteAllText(ScoreFilename, scoreJson);
                //выводим сообщение с результатом игрока
                await bot.SendTextMessageAsync(chatId, $"Ваш результат теперь равен: {UserScores[fromId].Score}");
                //задаём новый вопрос
                await NewRound(chatId);
            }
        }
        //метод, который задаёт новый вопрос
        public static async Task NewRound(long chatId)
        {
            //если нет state данного пользователя в списке 
            if (!UserStates.TryGetValue(chatId, out var state))
            {
                //создаём и инициализируем
                state = new QuestionState();
                UserStates[chatId] = state;
            }
            //задаём новый вопрос
            state.CurrentItem = quiz.NextQuestion();
            //обнуляем открытые буквы в подсказке
            state.Opened = 0;
            //записываем состояние в файл
            var stateJson = JsonConvert.SerializeObject(UserStates);
            System.IO.File.WriteAllText(StateFilename, stateJson);
            //отправляем вопрос
            await bot.SendTextMessageAsync(chatId, state.DisplayQuestion);
        }
        //метод который возвращает кнопки, которые показываем при базовых командах
        private static IReplyMarkup? GetDefaultReplyMarkup()
        {
            //Keyboard - список из списка кнопок
            var Keyboard = new List<List<KeyboardButton>>
                {
                    new List<KeyboardButton> {new KeyboardButton(QuizButtonText) {}, new KeyboardButton(StartButtonText) {} },
                    new List<KeyboardButton> {new KeyboardButton(TopButtonText) {}, new KeyboardButton(ResultButtonText) {} },
                    new List<KeyboardButton> {new KeyboardButton(ResetButtonText) {}, new KeyboardButton(FeedbackButtonText) {} },
                };
            ReplyKeyboardMarkup replyMarkup = new ReplyKeyboardMarkup(Keyboard);
            return replyMarkup;
        }
        //метод-обработчик ошибок
        public static async Task HandleErrorAsync(ITelegramBotClient bot, Exception exception, CancellationToken cancellationToken)
        {
            // Выводим ошибку в консоль и чат 
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(exception));
            await bot.SendTextMessageAsync(chatId, Newtonsoft.Json.JsonConvert.SerializeObject(exception));
        }
        //функция main
        static void Main(string[] args)
        {
            //создаём новый объекть quiz и указываем файл data.txt(хранится в корне) в качестве источника вопросов
            quiz = new Quiz("data.txt");
            //инициализируем словари
            UserStates = new Dictionary<long, QuestionState>();
            UserScores = new Dictionary<long, UserScore>();
            //считываем имеющиеся состояния из файла в переменную stateJson
            var stateJson = System.IO.File.ReadAllText(StateFilename);
            //форматируем джсон под нужный словарь и записываем состояния в словарь UserStates
            UserStates = JsonConvert.DeserializeObject<Dictionary<long, QuestionState>>(stateJson);
            //считываем результаты пользователей из файла в переменную scoreJson
            var scoreJson = System.IO.File.ReadAllText(ScoreFilename);
            //форматируем джсон под нужный словарь и записываем результаты в словарь UserScores
            UserScores = JsonConvert.DeserializeObject<Dictionary<long, UserScore>>(scoreJson);
            //выводим в консоль сообщение о том что бот запущен и его имя
            Console.WriteLine("Запущен бот " + bot.GetMeAsync().Result.FirstName);
            var cts = new CancellationTokenSource();
            var cancellationToken = cts.Token;
            var receiverOptions = new ReceiverOptions
            {
                AllowedUpdates = { }, // receive all update types
            };
            //начинаем получать апдейты от сервера
            bot.StartReceiving(
                HandleUpdateAsync,
                HandleErrorAsync,
                receiverOptions,
                cancellationToken
            );
            //ожидание завершения работы программы
            Console.ReadLine();
            //при завершении работы программы снова сохраняем состояния и результаты в соответствующие файлы
            stateJson = JsonConvert.SerializeObject(UserStates);
            System.IO.File.WriteAllText(StateFilename, stateJson);
            scoreJson = JsonConvert.SerializeObject(UserScores);
            System.IO.File.WriteAllText(ScoreFilename, scoreJson);
        }
    }
    //класс QuestionItem, объект которого представляем собой вопрос, состоящий из свойств Question и Answer
    public class QuestionItem
    {
        public string? Question { get; set; }
        public string? Answer { get; set; }
    }
    //класс Quiz, который хранит список вопросов, считанных из файла
    public class Quiz
    {
        //свойство список вопросов
        public List<QuestionItem> Questions { get; set; }
        //поля, необходимые для форматирования и работы методов
        private Random random;
        private int count;
        //конструктор создаёт список вопросов и инициализирует поля, передаём в него путь к файлу с вопросами, по умолчанию "data.txt"
        public Quiz(string path = "data.txt")
        {
            //считываем все вопросы из файла, в файле вопрос и ответ разделён вертикальной линией '|'
            var lines = System.IO.File.ReadAllLines(path);
            /*
              форматируем вопросы: для каждого элемента массива lines, метод Split возвращает массив line[2] из вопроса и ответа, 
              затем для каждого массива line мы создаём объект QuestionItem и присваиваем его полям соответствующие значения Question и Answer,
              и добавляем в список
            */
            Questions = lines
                .Select(line => line.Split(separator: '|'))
                .Select(line => new QuestionItem()
                {
                    Question = line[0],
                    Answer = line[1]
                }).ToList();
            //инициализируем поля
            random = new Random();
            count = Questions.Count;
        }
        //метод, который возвращает случайный вопрос, который при этом не может повторяться
        public QuestionItem NextQuestion()
        {
            //если прокручены все вопросы, то обновляем счётчик
            if (count < 1)
            {
                count = Questions.Count;
            }
            //генерируем случайное число, не больше количества вопросов - 1
            var index = new Random().Next(maxValue: count - 1);
            //берём случайный вопрос по сгенерированному индексу
            var question = Questions[index];
            //удаляем вопрос из его текущего места в списке
            Questions.RemoveAt(index);
            //добавляем его в конец списка
            Questions.Add(question);
            //отнимаем единицу от общего количества вопросов, чтобы не мог выпасть индекс вопроса, добавленного в конец
            count--;
            //возвращаем взятый вопрос
            return question;
        }
    }
    //Класс, который обозначает состояния вопроса, он позволяет боту помнить, какой вопрос задан в каком чате и на какой стадии находится данный вопрос
    public class QuestionState
    {
        //свойство - текущий вопрос
        public QuestionItem? CurrentItem { get; set; }
        //свойство - количество открытых букв в подсказке
        public int Opened { get; set; }
        //создаём подсказку, которую показываем после каждого вопроса: сначала выделяем количество открытых букв, а оставшиеся заменяем нижними подчеркиваниями '_'
        public string AnswerHint => CurrentItem.Answer
                       .Substring(0, Opened)
                       .PadRight(CurrentItem.Answer.Length, '_');
        //стрелочный метод, который возвращает набор строк, состоящий из вопроса, его длины и подсказки
        public string DisplayQuestion => $"{CurrentItem.Question}: {CurrentItem.Answer.Length} букв\n{AnswerHint}.";
        //булевая переменная, которая возвращает true, в случае, если в подсказке открыты все буквы, и false в ином случае
        public bool isEnd => Opened == CurrentItem.Answer.Length;
    }
    //класс, который нужен для хранения результатов пользователей 
    public class UserScore
    {
        //свойство, которое обозначает число очков
        public int Score { get; set; }
        //свойство, которое хранит имя пользователя
        public string Username { get; set; }
    }
}