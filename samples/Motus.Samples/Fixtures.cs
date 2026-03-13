namespace Motus.Samples;

/// <summary>
/// Shared inline HTML fixtures used across test classes.
/// All tests use SetContentAsync with these constants so they run without external dependencies.
/// </summary>
public static class Fixtures
{
    /// <summary>
    /// A fully functional todo app with add, complete, clear-completed, and active count.
    /// </summary>
    public const string TodoApp = """
        <!DOCTYPE html>
        <html>
        <head><title>Todo App</title></head>
        <body>
            <h1>Todo App</h1>
            <div>
                <input id="new-todo" placeholder="What needs to be done?" aria-label="New Todo" data-testid="new-todo-input" />
                <button id="add-btn" onclick="addTodo()">Add</button>
            </div>
            <ul id="todo-list"></ul>
            <div>
                <span id="active-count" data-testid="active-count">0 items left</span>
                <button id="clear-completed" onclick="clearCompleted()">Clear completed</button>
            </div>
            <script>
                function addTodo() {
                    const input = document.getElementById('new-todo');
                    const text = input.value.trim();
                    if (!text) return;
                    const li = document.createElement('li');
                    li.className = 'todo-item';
                    const cb = document.createElement('input');
                    cb.type = 'checkbox';
                    cb.onchange = function() {
                        li.classList.toggle('completed', cb.checked);
                        updateCount();
                    };
                    const span = document.createElement('span');
                    span.textContent = text;
                    li.appendChild(cb);
                    li.appendChild(span);
                    document.getElementById('todo-list').appendChild(li);
                    input.value = '';
                    updateCount();
                }
                function clearCompleted() {
                    document.querySelectorAll('.todo-item.completed').forEach(el => el.remove());
                    updateCount();
                }
                function updateCount() {
                    const active = document.querySelectorAll('.todo-item:not(.completed)').length;
                    document.getElementById('active-count').textContent = active + ' items left';
                }
            </script>
        </body>
        </html>
        """;

    /// <summary>
    /// A login form with email, password, remember-me checkbox, role select, and validation feedback.
    /// </summary>
    public const string LoginForm = """
        <!DOCTYPE html>
        <html>
        <head><title>Login</title></head>
        <body>
            <h1>Login</h1>
            <form id="login-form" onsubmit="handleSubmit(event)">
                <div>
                    <label for="email">Email</label>
                    <input id="email" type="email" placeholder="you@example.com" aria-label="Email" />
                </div>
                <div>
                    <label for="password">Password</label>
                    <input id="password" type="password" placeholder="Enter password" aria-label="Password" />
                </div>
                <div>
                    <label><input id="remember" type="checkbox" /> Remember me</label>
                </div>
                <div>
                    <label for="role">Role</label>
                    <select id="role" aria-label="Role">
                        <option value="">-- Select --</option>
                        <option value="admin">Admin</option>
                        <option value="user">User</option>
                        <option value="guest">Guest</option>
                    </select>
                </div>
                <button type="submit">Sign In</button>
                <div id="feedback" style="display:none; color:green;"></div>
            </form>
            <script>
                function handleSubmit(e) {
                    e.preventDefault();
                    const email = document.getElementById('email').value;
                    const feedback = document.getElementById('feedback');
                    feedback.textContent = 'Welcome, ' + email + '!';
                    feedback.style.display = 'block';
                }
            </script>
        </body>
        </html>
        """;

    /// <summary>
    /// A dashboard with cards, nav links, toggleable sidebar, and styled elements for CSS assertions.
    /// </summary>
    public const string Dashboard = """
        <!DOCTYPE html>
        <html>
        <head><title>Dashboard</title></head>
        <body>
            <nav>
                <a href="/home" data-testid="nav-home">Home</a>
                <a href="/reports" data-testid="nav-reports">Reports</a>
                <a href="/settings" data-testid="nav-settings">Settings</a>
            </nav>
            <h1>Dashboard</h1>
            <button id="toggle-sidebar" onclick="toggleSidebar()">Toggle Sidebar</button>
            <aside id="sidebar" style="display:block; background-color: rgb(240, 240, 240); padding: 16px;">
                <h2>Sidebar</h2>
                <p>Navigation panel</p>
            </aside>
            <div class="cards">
                <div class="card" data-testid="card-revenue">
                    <h3>Revenue</h3>
                    <p>$12,345</p>
                </div>
                <div class="card" data-testid="card-users">
                    <h3>Users</h3>
                    <p>1,234</p>
                </div>
                <div class="card" data-testid="card-orders">
                    <h3>Orders</h3>
                    <p>567</p>
                </div>
            </div>
            <script>
                function toggleSidebar() {
                    const sidebar = document.getElementById('sidebar');
                    sidebar.style.display = sidebar.style.display === 'none' ? 'block' : 'none';
                }
            </script>
        </body>
        </html>
        """;

    /// <summary>
    /// A minimal page with JS that fetches /api/data and renders results. Used for network mocking tests.
    /// </summary>
    public const string ApiPage = """
        <!DOCTYPE html>
        <html>
        <head><title>API Demo</title></head>
        <body>
            <h1>API Demo</h1>
            <button id="fetch-btn" onclick="fetchData()">Load Data</button>
            <div id="result"></div>
            <div id="error" style="display:none; color:red;"></div>
            <script>
                async function fetchData() {
                    try {
                        const res = await fetch('/api/data');
                        if (!res.ok) {
                            document.getElementById('error').textContent = 'Error: ' + res.status;
                            document.getElementById('error').style.display = 'block';
                            document.getElementById('result').textContent = '';
                            return;
                        }
                        const data = await res.json();
                        document.getElementById('result').textContent = data.items.join(', ');
                        document.getElementById('error').style.display = 'none';
                    } catch (e) {
                        document.getElementById('error').textContent = 'Network error';
                        document.getElementById('error').style.display = 'block';
                        document.getElementById('result').textContent = '';
                    }
                }
            </script>
        </body>
        </html>
        """;
}
