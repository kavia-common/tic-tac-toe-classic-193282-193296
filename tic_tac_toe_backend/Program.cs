using System.ComponentModel.DataAnnotations;

namespace TicTacToeBackend;

/// <summary>
/// Represents the two players in Tic Tac Toe.
/// </summary>
public enum Player
{
    X,
    O
}

/// <summary>
/// Status of the game.
/// </summary>
public enum GameStatus
{
    InProgress,
    Draw,
    XWon,
    OWon
}

/// <summary>
/// Game entity containing board state and metadata.
/// </summary>
public class Game
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public char[] Board { get; set; } = Enumerable.Repeat(' ', 9).ToArray();
    public Player CurrentTurn { get; set; } = Player.X; // X starts
    public GameStatus Status { get; set; } = GameStatus.InProgress;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public char WinnerSymbol =>
        Status switch
        {
            GameStatus.XWon => 'X',
            GameStatus.OWon => 'O',
            _ => ' '
        };
}

/// <summary>
/// Store for games in memory.
/// </summary>
public class GameStore
{
    private readonly Dictionary<Guid, Game> _games = new();

    public Game Create()
    {
        var g = new Game();
        _games[g.Id] = g;
        return g;
    }

    public bool TryGet(Guid id, out Game? game) => _games.TryGetValue(id, out game);
}

/// <summary>
/// Request payload for making a move.
/// </summary>
public class MoveRequest
{
    [Required]
    public string Player { get; set; } = default!; // "X" or "O"

    [Range(0, 8, ErrorMessage = "Position must be between 0 and 8.")]
    public int Position { get; set; }
}

/// <summary>
/// Response model for a game state.
/// </summary>
public class GameResponse
{
    public Guid Id { get; set; }
    public string[] Board { get; set; } = Array.Empty<string>();
    public string CurrentTurn { get; set; } = "X";
    public string Status { get; set; } = "InProgress";
    public string? Winner { get; set; }

    public static GameResponse FromGame(Game g) => new GameResponse
    {
        Id = g.Id,
        Board = g.Board.Select(c => c == ' ' ? "" : c.ToString()).ToArray(),
        CurrentTurn = g.CurrentTurn.ToString(),
        Status = g.Status.ToString(),
        Winner = g.WinnerSymbol == ' ' ? null : g.WinnerSymbol.ToString()
    };
}

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddOpenApiDocument(settings =>
        {
            settings.Title = "Tic Tac Toe Backend API";
            settings.Description = "Simple Tic Tac Toe game service with in-memory storage.";
            settings.Version = "1.0.0";
        });

        // Add CORS
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowAll", policy =>
            {
                policy.SetIsOriginAllowed(_ => true)
                      .AllowCredentials()
                      .AllowAnyMethod()
                      .AllowAnyHeader();
            });
        });

        // In-memory singleton game store
        builder.Services.AddSingleton<GameStore>();

        var app = builder.Build();

        // Use CORS
        app.UseCors("AllowAll");

        // Configure OpenAPI/Swagger
        app.UseOpenApi();
        app.UseSwaggerUi(config =>
        {
            config.Path = "/docs";
        });

        // Health check endpoint
        // PUBLIC_INTERFACE
        app.MapGet("/", () => new { message = "Healthy" })
           .WithName("HealthCheck")
           .WithTags("System")
           .WithSummary("Health check")
           .WithDescription("Returns a simple healthy message for uptime checks.");

        // Routes

        // PUBLIC_INTERFACE
        app.MapPost("/games", (GameStore store) =>
        {
            /**
             Creates a new Tic Tac Toe game.
             Returns:
               201 with the newly created game state.
            */
            var game = store.Create();
            return Results.Created($"/games/{game.Id}", GameResponse.FromGame(game));
        })
        .WithName("CreateGame")
        .WithTags("Games")
        .WithSummary("Create a new game")
        .WithDescription("Creates a new Tic Tac Toe game and returns its initial state.")
        .Produces<GameResponse>(StatusCodes.Status201Created);

        // PUBLIC_INTERFACE
        app.MapGet("/games/{id:guid}", (Guid id, GameStore store) =>
        {
            /**
             Gets the current state of a game by ID.
             Parameters:
               - id: Guid of the game
             Returns:
               200 with game state or 404 if not found.
            */
            if (!store.TryGet(id, out var game) || game is null)
                return Results.NotFound(new { error = "Game not found" });

            return Results.Ok(GameResponse.FromGame(game));
        })
        .WithName("GetGame")
        .WithTags("Games")
        .WithSummary("Get game state")
        .WithDescription("Fetch the current state of the specified game.")
        .Produces<GameResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        // PUBLIC_INTERFACE
        app.MapPost("/games/{id:guid}/moves", (Guid id, MoveRequest move, GameStore store) =>
        {
            /**
             Makes a move for the specified game.
             Body:
               - player: "X" or "O"
               - position: 0..8 (index on 3x3 board left-to-right, top-to-bottom)
             Rules:
               - X starts; enforce alternating turns.
               - Position must be empty.
               - Reject moves when game is not InProgress.
             Returns:
               200 with updated game state or a 4xx with error message.
            */
            if (!store.TryGet(id, out var game) || game is null)
                return Results.NotFound(new { error = "Game not found" });

            // Validate game status
            if (game.Status != GameStatus.InProgress)
                return Results.BadRequest(new { error = "Game is already complete" });

            // Validate payload
            if (string.IsNullOrWhiteSpace(move.Player))
                return Results.BadRequest(new { error = "Player is required (X or O)" });

            var playerStr = move.Player.Trim().ToUpperInvariant();
            if (playerStr != "X" && playerStr != "O")
                return Results.BadRequest(new { error = "Player must be 'X' or 'O'" });

            if (move.Position < 0 || move.Position > 8)
                return Results.BadRequest(new { error = "Position must be between 0 and 8" });

            var player = playerStr == "X" ? Player.X : Player.O;
            if (player != game.CurrentTurn)
                return Results.BadRequest(new { error = $"It is not {playerStr}'s turn" });

            if (game.Board[move.Position] != ' ')
                return Results.BadRequest(new { error = "Cell already occupied" });

            // Apply move
            game.Board[move.Position] = player == Player.X ? 'X' : 'O';

            // Check game status and update
            UpdateStatus(game);

            // Toggle turn if still in progress
            if (game.Status == GameStatus.InProgress)
            {
                game.CurrentTurn = game.CurrentTurn == Player.X ? Player.O : Player.X;
            }

            return Results.Ok(GameResponse.FromGame(game));
        })
        .WithName("MakeMove")
        .WithTags("Moves")
        .WithSummary("Make a move")
        .WithDescription("Places the specified player's symbol at the given board position if valid.")
        .Produces<GameResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);

        // PUBLIC_INTERFACE
        app.MapGet("/games/{id:guid}/status", (Guid id, GameStore store) =>
        {
            /**
             Gets the current status/winner of the specified game.
             Returns:
               200 with status and winner (if any) or 404 if not found.
            */
            if (!store.TryGet(id, out var game) || game is null)
                return Results.NotFound(new { error = "Game not found" });

            var resp = new
            {
                id = game.Id,
                status = game.Status.ToString(),
                winner = game.WinnerSymbol == ' ' ? null : game.WinnerSymbol.ToString(),
                currentTurn = game.Status == GameStatus.InProgress ? game.CurrentTurn.ToString() : null
            };
            return Results.Ok(resp);
        })
        .WithName("GetStatus")
        .WithTags("Games")
        .WithSummary("Get game status")
        .WithDescription("Returns the game status and winner if the game has concluded.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        app.Run();
    }

    private static bool IsWinning(char[] b, char s)
    {
        int[][] lines =
        [
            // rows
            [0,1,2],[3,4,5],[6,7,8],
            // cols
            [0,3,6],[1,4,7],[2,5,8],
            // diags
            [0,4,8],[2,4,6]
        ];
        return lines.Any(line => line.All(i => b[i] == s));
    }

    private static void UpdateStatus(Game g)
    {
        if (IsWinning(g.Board, 'X'))
        {
            g.Status = GameStatus.XWon;
            return;
        }
        if (IsWinning(g.Board, 'O'))
        {
            g.Status = GameStatus.OWon;
            return;
        }
        if (g.Board.All(c => c != ' '))
        {
            g.Status = GameStatus.Draw;
            return;
        }
        g.Status = GameStatus.InProgress;
    }
}
