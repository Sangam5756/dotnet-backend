using backend.Data;
using Microsoft.EntityFrameworkCore;
using backend.Middleware; // Import the namespace for the middleware
using Course_Backend.Models;


var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAllOrigins", policy =>
    {
        policy.AllowAnyOrigin()      // Allow any origin (frontend URL)
              .AllowAnyMethod()      // Allow any HTTP method (GET, POST, etc.)
              .AllowAnyHeader();     // Allow any headers
    });
});

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Add services to the container.

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
app.UseCors("AllowAllOrigins");

app.UseWhen(context => context.Request.Path.StartsWithSegments("/users/purchase"), appBuilder =>
{
    appBuilder.UseMiddleware<BasicAuthMiddleware>();
});

app.UseWhen(context => context.Request.Path.StartsWithSegments("/admin"), appBuilder =>
{   appBuilder.UseMiddleware<BasicAuthMiddleware>();
});


// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseHttpsRedirection();

{/*** USERS END-POINTS ***/ }

// Create a New User - Signup

app.MapPost("/user/signup", async (AppDbContext db, User user) =>
{

    var existingUser = await db.Users.FirstOrDefaultAsync(u => u.Username == user.Username);
    if (existingUser != null)
    {
        return Results.BadRequest("Username already exists. Please log in.");
    }
    else
    {

        db.Users.Add(user);
        await db.SaveChangesAsync();
        return Results.Created($"/user/{user.Id}", user);
    }
});

// Login a User

app.MapPost("/user/login", async (AppDbContext db, User loginUser) =>
{
    // Find the user by their username
    var user = await db.Users.FirstOrDefaultAsync(u => u.Username == loginUser.Username);

    // Check if user exists and password matches
    if (user == null || user.Password != loginUser.Password)
    {
        return Results.BadRequest("Invalid username or password.");
    }

    // If login is successful, return a success message and user information (without password)
    return Results.Ok(new { message = "Login successful", username = user.Username,role= user.Role });
});


//  Get All Purchased Course

app.MapGet("/users/purchase", async (HttpContext context, AppDbContext db) =>
{
    // Check if the user is authenticated
    var user = context.Items["User"] as User;

    if (user == null)
    {
        return Results.Problem("Invalid credentials");
    }

    // Fetch the user and their purchased courses from the database
    var dbUser = await db.Users
        .Include(u => u.PurchasedCourses)
        .FirstOrDefaultAsync(u => u.Id == user.Id);

    if (dbUser == null)
    {
        return Results.NotFound($"User with ID {user.Id} not found.");
    }

    return Results.Ok(dbUser.PurchasedCourses);
});

// Purchased Course
app.MapPost("/users/purchase", async (HttpContext context, AppDbContext db, CoursePurchaseRequest request) =>
{
    var user = context.Items["User"] as User;

    if (user == null)
    {
        return Results.Problem("User not authenticated.");
    }

    // Fetch the user from the database
    var dbUser = await db.Users.Include(u => u.PurchasedCourses)
                               .FirstOrDefaultAsync(u => u.Id == user.Id);

    if (dbUser == null)
    {
        return Results.NotFound("User not found.");
    }

    // Check if the course is already purchased
    var existingCourse = dbUser.PurchasedCourses.FirstOrDefault(c => c.Id == request.CourseId);
    if (existingCourse != null)
    {
        return Results.Conflict("Course already purchased.");
    }

    // Fetch the course from the database
    var course = await db.Courses.FindAsync(request.CourseId);
    if (course == null)
    {
        return Results.NotFound("Course not found.");
    }

    // Add the course to the user's purchased courses
    dbUser.PurchasedCourses.Add(course);
    await db.SaveChangesAsync();

    return Results.Ok(new { message = "Course purchased successfully" });
});

// Update A Purchase Course
    app.MapPut("/users/purchase", async (AppDbContext db, HttpContext context, List<int> courseIds) =>
{
    // Retrieve authenticated user from the context
    var user = context.Items["User"] as User;

    Console.WriteLine($"Authenticated userINSIDE PURCHASE: {user?.Username}");

    if (user == null)
    {
        return Results.NotFound("User not found in context. Please ensure you are authenticated.");
    }
    var dbUser = await db.Users
       .Include(u => u.PurchasedCourses)
       .FirstOrDefaultAsync(u => u.Username == user.Username);

    // Fetch new courses from courseIds
    var courses = await db.Courses.Where(c => courseIds.Contains(c.Id)).ToListAsync();

    if (courses.Count != courseIds.Count)
    {
        return Results.BadRequest("Some of the provided courses are invalid.");
    }

    // Update user's purchased courses
    // user.PurchasedCourses.Clear();
    foreach (var course in courses)
    {
        dbUser?.PurchasedCourses?.Add(course);
    }

    // Save changes to the database
    await db.SaveChangesAsync();

    return Results.Ok(user);
});

// Delete The Purchase Course by it's ID
app.MapDelete("/users/purchase/{courseId}", async (AppDbContext db, HttpContext context, int courseId) =>
{
    // Retrieve the authenticated user from the context (set by middleware)
    var user = context.Items["User"] as User;

    if (user == null)
    {
        return Results.Unauthorized();
    }

    // Include the user's purchased courses to enable deletion
    var dbUser = await db.Users
        .Include(u => u.PurchasedCourses)
        .FirstOrDefaultAsync(u => u.Id == user.Id);

    if (dbUser == null)
    {
        return Results.NotFound("User not found.");
    }

    // Find the course to be removed from the user's purchased courses list
    var course = dbUser.PurchasedCourses.FirstOrDefault(c => c.Id == courseId);

    if (course == null)
    {
        return Results.NotFound($"Course with ID {courseId} not found in the user's purchased courses.");
    }

    // Remove the course from the user's purchased courses
    dbUser.PurchasedCourses.Remove(course);

    // Save changes to the database
    await db.SaveChangesAsync();

    return Results.Ok($"Course with ID {courseId} has been removed from the user's purchased courses.");
});

// Get all courses
app.MapGet("/courses", async (AppDbContext db) => await db.Courses.ToListAsync());


{/*** Admin END-POINTS ***/ }



// Get all users
app.MapGet("/admin/user", async (AppDbContext db,HttpContext context) =>{

        var user = context.Items["User"] as User;
        
        if(user?.Role == "admin"){

                var users =  await db.Users.ToListAsync();
                 return Results.Ok(users);

        }else{

            return Results.BadRequest("please login as admin.");
        }

   
    


});

// create a new course
app.MapPost("/admin/courses", async (AppDbContext db, HttpContext context,Course course) =>
{

            var user = context.Items["User"] as User;

         
         if(user.Role == "admin"){

         

    var existingCourse = await db.Courses.FirstOrDefaultAsync(c => c.Title == course.Title);


    if (existingCourse != null)
    {
        return Results.Ok(new { message = "Course Already exists" });
    }

    // 2. Validate required fields
    if (string.IsNullOrWhiteSpace(course.Title) || string.IsNullOrWhiteSpace(course.Description))
    {
        return Results.BadRequest("Title and Description are required.");
    }

    // 3. Validate positive price
    if (course.Price <= 0)
    {
        return Results.BadRequest("Price must be a positive value.");
    }


    db.Courses.Add(course);
    await db.SaveChangesAsync();
    return Results.Created($"/courses/{course.Id}", course);

         }else{
                        return Results.BadRequest("please login as admin.");


         }
});



// Update an existing course
app.MapPut("/admin/courses/{id}", async (AppDbContext db,HttpContext context, int id, Course updatedCourse) =>
{

            var user = context.Items["User"] as User;

         if(user.Role == "admin"){

    

    // Find the course by id
    var existingCourse = await db.Courses.FindAsync(id);

    if (existingCourse == null)
    {
        return Results.NotFound(new { message = "Course not found." });
    }

    // 1. Validate required fields
    if (string.IsNullOrWhiteSpace(updatedCourse.Title) || string.IsNullOrWhiteSpace(updatedCourse.Description))
    {
        return Results.BadRequest("Title and Description are required.");
    }

    // 2. Validate positive price
    if (updatedCourse.Price <= 0)
    {
        return Results.BadRequest("Price must be a positive value.");
    }

    // 3. Check if the title is changing and if the new title already exists
    if (updatedCourse.Title != existingCourse.Title)
    {
        var titleExists = await db.Courses.AnyAsync(c => c.Title == updatedCourse.Title);
        if (titleExists)
        {
            return Results.BadRequest(new { message = "Course with this title already exists." });
        }
    }

    // 4. Update the course details
    existingCourse.Title = updatedCourse.Title;
    existingCourse.Description = updatedCourse.Description;
    existingCourse.Price = updatedCourse.Price;

    // 5. Save changes
    await db.SaveChangesAsync();

    return Results.Ok(existingCourse);

    }
    else{

        return Results.BadRequest("please login as admin");
    }
});

// Delete All courses
app.MapDelete("/admin/courses", async (AppDbContext db) =>
{
    // Fetch all courses
    var allCourses = await db.Courses.ToListAsync();
    
    if (!allCourses.Any())
    {
        return Results.NotFound("No courses found to delete.");
    }

    // Remove all courses from the database
    db.Courses.RemoveRange(allCourses);
    await db.SaveChangesAsync();

    return Results.Ok("All courses have been deleted successfully.");
});

// Delete Course By it's ID
app.MapDelete("/admin/courses/{id}", async (int id, AppDbContext db) =>
{
    // Find the course by its id
    var course = await db.Courses.FindAsync(id);

    // If the course does not exist, return a NotFound result
    if (course == null)
    {
        return Results.NotFound($"Course with ID {id} not found.");
    }

    // Remove the course and save changes
    db.Courses.Remove(course);
    await db.SaveChangesAsync();

    return Results.Ok($"Course with ID {id} has been deleted.");
});

// Delete All users
app.MapDelete("/admin/user", async (AppDbContext db) =>
{
    var allUsers = await db.Users.ToListAsync();

    if (allUsers.Count == 0)
    {
        return Results.NotFound("No users found to delete.");
    }

    foreach (var user in allUsers)
    {
        db.Users.Remove(user);
    }

    await db.SaveChangesAsync();

    return Results.Ok("All users have been deleted.");
});

// Delete a user by ID
app.MapDelete("/admin/user/{userId}", async (int userId, AppDbContext db) =>
{
    // Find the user by ID
    var user = await db.Users.FindAsync(userId);

    // Check if the user exists
    if (user == null)
    {
        return Results.NotFound($"User with ID {userId} not found.");
    }

    // Remove the user from the database
    db.Users.Remove(user);
    await db.SaveChangesAsync();

    return Results.Ok($"User with ID {userId} has been deleted.");
});

// Add more endpoints as needed
app.Run();
