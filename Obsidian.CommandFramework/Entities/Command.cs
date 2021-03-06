﻿using Obsidian.CommandFramework.Attributes;
using Obsidian.CommandFramework.Exceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.CommandFramework.Entities
{
    public class Command
    {
        public string Name { get; private set; }

        public string[] Aliases { get; private set; }

        public Command Parent { get; private set; }

        public string Description { get; private set; }

        public BaseExecutionCheckAttribute[] ExecutionChecks { get; private set; }

        public List<MethodInfo> Overloads { get; internal set; }

        internal CommandHandler Handler { get; set; }

        public Command(string name, string[] aliases, string description, Command parent, BaseExecutionCheckAttribute[] checks, CommandHandler handler)
        {
            this.Name = name;
            this.Aliases = aliases;
            this.Parent = parent;
            this.ExecutionChecks = checks;
            this.Handler = handler;
            this.Overloads = new List<MethodInfo>();
            this.Description = description;
        }

        public bool CheckCommand(string[] input, Command parent)
        {
            if (this.Parent == parent && input.Count() > 0)
            {
                if (this.Name == input[0])
                {
                    return true;
                }
                else if (this.Aliases.Count() > 0)
                {
                    return this.Aliases.Contains(input[0]);
                }
            }

            return false;
        }

        /// <summary>
        /// Gets the full qualified command name.
        /// </summary>
        /// <returns>Full qualified command name.</returns>
        public string GetQualifiedName()
        {
            var c = this;
            string name = c.Name;

            while (c.Parent != null)
            {
                name = $"{c.Parent.Name} {name}";
                c = c.Parent;
            }

            return name;
        }

        /// <summary>
        /// Executes this command.
        /// </summary>
        /// <typeparam name="T">Context type.</typeparam>
        /// <param name="Context">Execution context.</param>
        /// <returns></returns>
        public async Task ExecuteAsync<T>(T context, string[] args) where T : BaseCommandContext
        {
            // Find matching overload
            if (!this.Overloads.Any(x => x.GetParameters().Count() - 1 == args.Count()
             || x.GetParameters().Last().CustomAttributes.Any(y => typeof(RemainingAttribute).IsAssignableFrom(y.AttributeType))))
            {
                throw new InvalidCommandOverloadException($"No such overload for command {this.GetQualifiedName()}");
            }

            var method = this.Overloads.First(x => x.GetParameters().Count() - 1 == args.Count()
            || x.GetParameters().Last().CustomAttributes.Any(y => typeof(RemainingAttribute).IsAssignableFrom(y.AttributeType)));

            // create instance of declaring type to execute.
            var obj = Activator.CreateInstance(method.DeclaringType);

            // Get required params
            var methodparams = method.GetParameters().Skip(1).ToArray();

            // Set first parameter to be the context.
            var parsedargs = new object[methodparams.Length + 1];
            parsedargs[0] = context;

            // TODO comments
            for (int i = 0; i < methodparams.Length; i++)
            {
                // Current param and arg
                var paraminfo = methodparams[i];
                var arg = args[i];

                // This can only be true if we get a [Remaining] arg. Sets arg to remaining text.
                if (args.Length > methodparams.Length && i == methodparams.Length - 1)
                {
                    arg = string.Join(' ', args.Skip(i));
                }

                // Checks if there is any valid registered command handler
                if (this.Handler._argumentParsers.Any(x => x.GetType().BaseType.GetGenericArguments()[0] == paraminfo.ParameterType))
                {
                    // Gets parser
                    var parsertype = this.Handler._argumentParsers.First(x => x.GetType().BaseType.GetGenericArguments()[0] == paraminfo.ParameterType).GetType();
                    var parser = Activator.CreateInstance(parsertype);

                    // sets args for parser method
                    var parseargs = new object[3] { (object)arg, (object)context, null };

                    // cast with reflection?
                    if ((bool)parsertype.GetMethod("TryParseArgument").Invoke(parser, parseargs))
                    {
                        // parse success!
                        parsedargs[i + 1] = parseargs[2];
                    }
                    else
                    {
                        // Argument can't be parsed to the parser's type.
                        throw new CommandArgumentParsingException($"Argument '{arg}' was not parseable to {paraminfo.ParameterType.Name}!");
                    }
                }
                else
                {
                    throw new NoSuchParserException($"No valid argumentparser found for type {paraminfo.ParameterType.Name}!");
                }
            }

            // do execution checks
            var checks = method.CustomAttributes.Where(x => typeof(BaseExecutionCheckAttribute).IsAssignableFrom(x.AttributeType));

            foreach (var c in checks)
            {
                var check = (BaseExecutionCheckAttribute)Activator.CreateInstance(c.AttributeType);
                if (!await check.RunChecksAsync(context))
                {
                    // A check failed.
                    // TODO: Tell user what arg failed?
                    throw new CommandExecutionCheckException($"One or more execution checks failed.");
                }
            }

            // await the command with it's args
            var task = (Task)method.Invoke(obj, parsedargs);

            await task;
        }
    }
}
